namespace SimpleTGBot;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
public class TelegramBot
{
    
    private const string BotToken = "8657585744:AAEt4ddkGDvK2ptGQA9Nnf7OTGJnd4mQV4g";
    // Файлы проекта: задачи, простые AIML-ответы и логи.
    private const string TasksFile = "tasks.json";
    private const string AimlFile = "aiml_answers.json";
    private const string LogsFile = "logs.txt";
    
    public async Task Run()
    {
        CreateAimlFileIfNotExists();
        var botClient = new TelegramBotClient(BotToken);
        using CancellationTokenSource cts = new CancellationTokenSource();
        // Бот будет получать только обычные сообщения.
        ReceiverOptions options = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message }
        };
        botClient.StartReceiving(OnMessageReceived, OnErrorOccured, options, cts.Token);
        var me = await botClient.GetMeAsync(cancellationToken: cts.Token);
        Console.WriteLine($"Бот @{me.Username} запущен");
        Console.WriteLine("Нажми Esc для остановки");
        while (Console.ReadKey().Key != ConsoleKey.Escape) { }
        cts.Cancel();
    }
    
    async Task OnMessageReceived(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.Message == null) return;
        if (update.Message.Text == null) return;
        long chatId = update.Message.Chat.Id;
        string text = update.Message.Text.Trim();
        string lower = text.ToLower();
        AddLog($"{chatId}: {text}");
        if (lower == "/start")
        {
            await botClient.SendTextMessageAsync(chatId,
                "Привет! Я TODO-бот.\n" +
                "Я умею хранить список дел.\n\n" +
                "/add текст - добавить задачу\n" +
                "/list - показать задачи\n" +
                "/done номер - выполнить\n" +
                "/delete номер - удалить\n" +
                "/clear - очистить\n" +
                "/help - помощь", cancellationToken: ct);
        }
        else if (lower == "/help")
        {
            await botClient.SendTextMessageAsync(chatId,
                "Пример использования:\n" +
                "/add Например: Купить хлеб\n" +
                "/list\n" +
                "/done 1\n" +
                "/delete 1\n" +
                "/clear", cancellationToken: ct);
        }
        else if (lower.StartsWith("/add"))
        {
            // Берём текст после команды /add.
            string taskText = text.Substring(4).Trim();
            if (taskText == "")
            {
                await botClient.SendTextMessageAsync(chatId, "Напиши задачу после /add", cancellationToken: ct);
                return;
            }
            List<TodoTask> tasks = LoadTasks();
            TodoTask task = new TodoTask
            {
                UserId = chatId,
                Id = GetNextId(tasks, chatId),
                Text = taskText,
                IsDone = false
            };
            tasks.Add(task);
            SaveTasks(tasks);
            await botClient.SendTextMessageAsync(chatId, "Задача добавлена ✅", cancellationToken: ct);
        }
        else if (lower == "/list")
        {
            List<TodoTask> tasks = LoadTasks();
            List<TodoTask> userTasks = tasks.Where(t => t.UserId == chatId).ToList();
            if (userTasks.Count == 0)
            {
                await botClient.SendTextMessageAsync(chatId, "Список пуст", cancellationToken: ct);
                return;
            }
            string answer = "Твои задачи:\n";
            foreach (TodoTask task in userTasks)
            {
                string status = task.IsDone ? "✅" : "❌";
                answer += task.Id + ". " + task.Text + " " + status + "\n";
            }
            await botClient.SendTextMessageAsync(chatId, answer, cancellationToken: ct);
        }
        else if (lower.StartsWith("/done"))
        {
            // Проверяем, что пользователь написал номер задачи.
            string numberText = lower.Substring(5).Trim();
            if (!int.TryParse(numberText, out int number))
            {
                await botClient.SendTextMessageAsync(chatId, "Напиши номер, например /done 1", cancellationToken: ct);
                return;
            }
            List<TodoTask> tasks = LoadTasks();
            TodoTask? task = tasks.FirstOrDefault(t => t.UserId == chatId && t.Id == number);
            if (task == null)
            {
                await botClient.SendTextMessageAsync(chatId, "Такой задачи нет", cancellationToken: ct);
                return;
            }
            task.IsDone = true;
            SaveTasks(tasks);
            await botClient.SendTextMessageAsync(chatId, "Задача выполнена ✅", cancellationToken: ct);
        }
        else if (lower.StartsWith("/delete"))
        {
            string numberText = lower.Substring(7).Trim();
            if (!int.TryParse(numberText, out int number))
            {
                await botClient.SendTextMessageAsync(chatId, "Напиши номер, например /delete 1", cancellationToken: ct);
                return;
            }
            List<TodoTask> tasks = LoadTasks();
            TodoTask? task = tasks.FirstOrDefault(t => t.UserId == chatId && t.Id == number);
            if (task == null)
            {
                await botClient.SendTextMessageAsync(chatId, "Такой задачи нет", cancellationToken: ct);
                return;
            }
            tasks.Remove(task);
            SaveTasks(tasks);
            await botClient.SendTextMessageAsync(chatId, "Задача удалена", cancellationToken: ct);
        }
        else if (lower == "/clear")
        {
            List<TodoTask> tasks = LoadTasks();
            // Удаляем задачи только этого пользователя.
            tasks = tasks.Where(t => t.UserId != chatId).ToList();
            SaveTasks(tasks);
            await botClient.SendTextMessageAsync(chatId, "Список очищен", cancellationToken: ct);
        }
        else
        {
            // Если это не команда, ищем ответ в AIML-файле.
            string answer = FindAimlAnswer(lower);
            await botClient.SendTextMessageAsync(chatId, answer, cancellationToken: ct);
        }
    }
    
    Task OnErrorOccured(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        string message = exception switch
        {
            ApiRequestException apiEx => "Telegram API: " + apiEx.Message,
            _ => exception.Message
        };
        Console.WriteLine(message);
        AddLog("Ошибка: " + message);
        return Task.CompletedTask;
    }
    // Читаем задачи из json-файла.
    List<TodoTask> LoadTasks()
    {
        if (!System.IO.File.Exists(TasksFile)) return new List<TodoTask>();
        string json = System.IO.File.ReadAllText(TasksFile);
        if (json == "") return new List<TodoTask>();
        List<TodoTask>? tasks = JsonSerializer.Deserialize<List<TodoTask>>(json);
        return tasks ?? new List<TodoTask>();
    }
    // Сохраняем задачи в json-файл.
    void SaveTasks(List<TodoTask> tasks)
    {
        string json = JsonSerializer.Serialize(tasks, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(TasksFile, json);
    }
    // Номер новой задачи для пользователя.
    int GetNextId(List<TodoTask> tasks, long userId)
    {
        List<TodoTask> userTasks = tasks.Where(t => t.UserId == userId).ToList();
        if (userTasks.Count == 0) return 1;
        return userTasks.Max(t => t.Id) + 1;
    }
    //  запись логов.
    void AddLog(string text)
    {
        System.IO.File.AppendAllText(LogsFile, DateTime.Now + " | " + text + "\n");
    }
    // Создаём файл с AIML-ответами.
    void CreateAimlFileIfNotExists()
    {
        if (System.IO.File.Exists(AimlFile)) return;
        List<AimlAnswer> answers = new List<AimlAnswer>
        {
            new AimlAnswer
            {
                Patterns = new List<string> { "привет", "здравствуй", "hello" },
                Answers = new List<string> { "Привет! Напиши /help", "Здравствуй! Я TODO-бот" }
            },
            new AimlAnswer
            {
                Patterns = new List<string> { "спасибо", "спс", "благодарю" },
                Answers = new List<string> { "Пожалуйста", "Не за что" }
            },
            new AimlAnswer
            {
                Patterns = new List<string> { "помощь", "команды", "что ты умеешь" },
                Answers = new List<string> { "Я умею работать с задачами. Напиши /help" }
            }
        };
        string json = JsonSerializer.Serialize(answers, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(AimlFile, json);
    }
    // Ищем подходящую фразу в AIML.
    string FindAimlAnswer(string text)
    {
        string json = System.IO.File.ReadAllText(AimlFile);
        List<AimlAnswer>? answers = JsonSerializer.Deserialize<List<AimlAnswer>>(json);
        if (answers == null) return "Не понял. Напиши /help";
        foreach (AimlAnswer item in answers)
        {
            foreach (string pattern in item.Patterns)
            {
                if (text.Contains(pattern)) return item.Answers[0];
            }
        }
        return "Не понял. Напиши /help";
    }
}
public class TodoTask
{
    public long UserId { get; set; }
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public bool IsDone { get; set; }
}
public class AimlAnswer
{
    public List<string> Patterns { get; set; } = new List<string>();
    public List<string> Answers { get; set; } = new List<string>();
}
