using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;

namespace TelegramBotExperiments
{
    using System.Text;
    using Hangfire;
    using Hangfire.MemoryStorage;
    using Newtonsoft.Json;
    using Telegram.Bot.Polling;

    class Program
    {
        static ITelegramBotClient bot = new TelegramBotClient("6566550775:AAH3bi5csUbjyKyasA9Fdd0ccAN-2mPhiJY");
        private static UserState currentState = UserState.None;
        private static string Login = string.Empty;
        private static string Password =string.Empty;
        private static List<string> HeroesToSearch = new List<string>();
        private const string acc2sUri = "https://back-adm.acc2s.shop/v1/api/user/login";
        private const string apiUrl = "https://back-adm.acc2s.shop/v1/api/shop/account_search";
        private static readonly HttpClient client = new HttpClient();
        
        enum UserState
        {
            None,
            AwaitingLogin,
            AwaitingPassword,
            AwaitingHeroesToSearch
        }
        
        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Некоторые действия

            if(update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
            {
                var message = update.Message;

                if (message.Text.ToLower() == "/start")
                {
                    await botClient.SendTextMessageAsync(message.Chat.Id, "Lutsiv`s personal bot for acc2s!");
        
                    // Prompt user for their login
                    await botClient.SendTextMessageAsync(message.Chat.Id, "Please provide your login for acc2s:");
                    currentState = UserState.AwaitingLogin;
                    return;
                }

                switch(currentState)
                {
                    case UserState.AwaitingLogin:
                        // Save user's response as login
                        Login = message.Text;
                        Console.WriteLine($"Received login: {Login}");
            
                        await botClient.SendTextMessageAsync(message.Chat.Id, "Login received! Now, please provide your password for acc2s:");
                        currentState = UserState.AwaitingPassword;
                        break;

                    case UserState.AwaitingPassword:
                        // Save user's response as password
                        Password = message.Text;
                        Console.WriteLine($"Received password: {Password}");
            
                        await botClient.SendTextMessageAsync(message.Chat.Id, "Password received! Now, please provide your pairs heroes to search(Hero + Hero line by line):");
                        currentState = UserState.AwaitingHeroesToSearch;
                        break;

                    case UserState.AwaitingHeroesToSearch:
                        // Save user's response as heroesToSearch
                        HeroesToSearch = HeroesTextMessageToList(message.Text);
                        Console.WriteLine($"Received heroes to search");
            
                        await botClient.SendTextMessageAsync(message.Chat.Id, "Data received! Thank you.");
                        foreach (string heroPair in HeroesToSearch)
                        {
                            RecurringJob.AddOrUpdate(heroPair, () => SendRequests(botClient, update, heroPair), "*/5 * * * *");
                        }

                        currentState = UserState.None;
                        break;

                    default:
                        await botClient.SendTextMessageAsync(message.Chat.Id, "If you want to start, please type /start");
                        break;
                }
            }

        }

        public static async Task SendRequests(ITelegramBotClient botClient, Update update, string heroPair)
        {
            var message = update.Message;
            ///////////////////////////////////////////////////////////////////////login///////////////////////////////////////////////////////////////////////////
            var loginPayload = new 
            {
                username = Login,
                password = Password
            };

            string loginJsonPayload = JsonConvert.SerializeObject(loginPayload);
            HttpContent loginContent = new StringContent(loginJsonPayload, Encoding.UTF8, "application/json");
                        
            HttpResponseMessage loginResponse = await client.PostAsync(acc2sUri, loginContent);
                        
            string jwtToken = string.Empty;
                        
            if (loginResponse.IsSuccessStatusCode)
            {
                var loginResponseContent = await loginResponse.Content.ReadAsStringAsync();
                var jsonResponse = JsonConvert.DeserializeObject<dynamic>(loginResponseContent);
    
                // Extract token (Assuming the token is returned in a property called "token" in the response)
                jwtToken = jsonResponse.data.token;
            }
            else
            {
                Console.WriteLine("Failed to login.");
                await botClient.SendTextMessageAsync(message.Chat.Id, "Failed to login.");
            }
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            ///////////////////////////////////////////////////////////////////////search///////////////////////////////////////////////////////////////////////////
            var payload = new 
            {
                search = heroPair,
                filter = new 
                {
                    page = 1,
                    limit = 250,
                    min_price = 0,
                    max_price = 50000
                }
            };

            string jsonPayload = JsonConvert.SerializeObject(payload);
            HttpContent searchContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            if (!string.IsNullOrEmpty(jwtToken))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);
            }
            // Send the POST request
            HttpResponseMessage searchResponse = await client.PostAsync(apiUrl, searchContent);
            var responseText = await searchResponse.Content.ReadAsStringAsync();
            if (searchResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Successfully sent the request!");
                Console.WriteLine(responseText);
            }
            else
            {
                Console.WriteLine($"Request failed with status: {searchResponse.StatusCode}");
                Console.WriteLine(responseText);
            }
            // Deserialize the JSON response to a dynamic object.
            var searchResponseObj = JsonConvert.DeserializeObject<dynamic>(responseText);

            // Extract the count value.
            int accountCount = searchResponseObj.count;
            if (accountCount > 0)
            {
                await botClient.SendTextMessageAsync(message.Chat.Id, $"Found {accountCount} accounts! With heroes: {heroPair}");
            }
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        }

        private static List<string> HeroesTextMessageToList(string messageText)
        {
            List<string> result = messageText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Where(line => !string.IsNullOrWhiteSpace(line))
                                       .Select(line => line.Trim().Replace(" + ", ","))
                                       .ToList();

            return result;
        }

        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            // Некоторые действия
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(exception));
        }


        static void Main(string[] args)
        {
            // Initialize Hangfire configuration to use MemoryStorage
            GlobalConfiguration.Configuration.UseMemoryStorage();

            // Start the Hangfire server
            using (var server = new BackgroundJobServer())
            {
                Console.WriteLine("Hangfire Server started.");

                // Start the Telegram bot
                Console.WriteLine("Bot is running " + bot.GetMeAsync().Result.FirstName);

                // Telegram bot starts receiving updates
                var cts = new CancellationTokenSource();
                var cancellationToken = cts.Token;
                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = { }, // receive all update types
                };
                bot.StartReceiving(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    receiverOptions,
                    cancellationToken
                );

                Console.WriteLine("Press any key to exit...");
                Console.ReadLine();
            }
        }
    }
}