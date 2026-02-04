using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Spectre.Console;

namespace P2PFileDaemon
{
    class Program
    {
        private const int Port = 8888;
        private const int BufferSize = 1024 * 64;

        static async Task Main(string[] args)
        {
            Console.CancelKeyPress += (sender, e) => { e.Cancel = true; };

            while (true)
            {
                Console.Clear();
                AnsiConsole.Write(new FigletText("p2p daemon v1.4").Color(Color.LightGoldenrod1));
                AnsiConsole.MarkupLine("[grey]v1.4.1-salt-randomized | ip_upd_peer[/]");
                Console.WriteLine("-------------------------------------------");

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("> [lime]select_action:[/]")
                        .AddChoices(new[] { "push_file", "pull_file", "help", "exit" }));

                if (choice == "exit") break;

                try
                {
                    if (choice == "push_file") await RunSender();
                    else if (choice == "pull_file") await RunReceiver();
                    else if (choice == "help") ShowHelp();
                }
                catch (Exception ex)
                {
                    // Ловим любые ошибки сети аккуратно
                    AnsiConsole.MarkupLine($"\n[red]err:[/] {ex.Message.ToLower()}");
                }

                AnsiConsole.MarkupLine("\n[grey]>> press any key to return to menu...[/]");
                Console.ReadKey(true);
            }
        }

        static async Task RunSender()
        {
            var path = ReadInputWithEsc("src_path > ");
            if (string.IsNullOrEmpty(path)) return;
            path = path.Trim('"');
            if (!System.IO.File.Exists(path)) throw new Exception("file_not_found");

            var password = AnsiConsole.Ask<string>("set_secret_key > ");
            FileInfo info = new FileInfo(path);

            string sourceHash = "";
            await AnsiConsole.Status().StartAsync("[yellow]calculating_checksum...[/]", async ctx => {
                sourceHash = await CalculateHashAsync(path);
            });

            // =============================================================
            // НОВЫЙ БЛОК: ВЫБОР IP-АДРЕСА
            // =============================================================
            string selectedIp = SelectLocalIP();
            if (string.IsNullOrEmpty(selectedIp))
            {
                AnsiConsole.MarkupLine("[red]err:[/] no network interface selected.");
                return;
            }

            TcpListener listener = new TcpListener(IPAddress.Parse(selectedIp), Port); // Слушаем на ВЫБРАННОМ IP
            try
            {
                listener.Start();
                AnsiConsole.MarkupLine($"[grey]info:[/] listener_on: [yellow]{selectedIp}[/]:{Port}");
                AnsiConsole.MarkupLine("[blink yellow]wait:[/] awaiting_peer...");

                // ... остальной код RunSender остается без изменений ...
                using var client = await listener.AcceptTcpClientAsync();
                using var networkStream = client.GetStream();

                byte[] salt = RandomNumberGenerator.GetBytes(16);
                using Aes aes = Aes.Create();
                var keyMaterial = DeriveKey(password, salt);
                aes.Key = keyMaterial.key;
                aes.IV = keyMaterial.iv;

                var meta = new { name = info.Name, size = info.Length, hash = sourceHash };
                byte[] metaData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(meta) + "\n");
                await networkStream.WriteAsync(metaData);
                await networkStream.WriteAsync(salt);
                await networkStream.WriteAsync(aes.IV);

                byte[] encryptedAuth;
                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    await cs.WriteAsync(Encoding.UTF8.GetBytes("AUTH_OK"));
                    await cs.FlushFinalBlockAsync();
                    encryptedAuth = ms.ToArray();
                }
                await networkStream.WriteAsync(BitConverter.GetBytes(encryptedAuth.Length));
                await networkStream.WriteAsync(encryptedAuth);

                AnsiConsole.MarkupLine("[grey]sync:[/] verifying_credentials...");
                byte[] ackBuffer = new byte[1];
                int ackRead = await networkStream.ReadAsync(ackBuffer, 0, 1);

                if (ackRead == 0 || ackBuffer[0] != 1)
                {
                    AnsiConsole.MarkupLine("[bold red]err:[/] peer_auth_failed.");
                    return;
                }

                AnsiConsole.MarkupLine("[green]auth:[/] approved. streaming_data...");
                using var cryptoStream = new CryptoStream(networkStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
                await AnsiConsole.Progress()
                    .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new DownloadedColumn(), new TransferSpeedColumn())
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask($"[grey]pushing:[/] {info.Name}");
                        task.MaxValue = info.Length;
                        using var fs = new System.IO.FileStream(path, FileMode.Open, FileAccess.Read);
                        byte[] buffer = new byte[BufferSize];
                        int read;
                        while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await cryptoStream.WriteAsync(buffer, 0, read);
                            task.Increment(read);
                        }
                    });

                await cryptoStream.FlushFinalBlockAsync();
                AnsiConsole.MarkupLine("[bold green]done:[/] transfer_complete");
            }
            finally { listener.Stop(); }
        }

        static async Task RunReceiver()
        {
            var ip = AnsiConsole.Ask<string>("target_ip > ");
            var password = AnsiConsole.Ask<string>("enter_secret_key > ");

            using var client = new TcpClient();
            await client.ConnectAsync(ip, Port);
            using var networkStream = client.GetStream();

            // 1. Читаем Метаданные
            StreamReader reader = new StreamReader(networkStream);
            string? rawMeta = await reader.ReadLineAsync();
            var meta = JsonSerializer.Deserialize<FileMeta>(rawMeta!);

            // 2. Читаем СОЛЬ (16 байт)
            byte[] salt = new byte[16];
            await ReadFullAsync(networkStream, salt, 16);

            // 3. Читаем IV (16 байт)
            byte[] iv = new byte[16];
            await ReadFullAsync(networkStream, iv, 16);

            // 4. Генерация ключа (используем полученную СОЛЬ + введенный Пароль)
            using Aes aes = Aes.Create();
            var keyMaterial = DeriveKey(password, salt);
            aes.Key = keyMaterial.key;
            aes.IV = iv;

            try
            {
                // 5. Проверяем AUTH TOKEN
                byte[] lenBuffer = new byte[4];
                await ReadFullAsync(networkStream, lenBuffer, 4);
                int authLen = BitConverter.ToInt32(lenBuffer);

                byte[] encryptedAuth = new byte[authLen];
                await ReadFullAsync(networkStream, encryptedAuth, authLen);

                using (var ms = new MemoryStream(encryptedAuth))
                using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs))
                {
                    string authResult = await sr.ReadToEndAsync();
                    if (authResult != "AUTH_OK") throw new Exception();
                }

                // 6. ЕСЛИ ВСЁ ОК -> ОТПРАВЛЯЕМ "ACK" (байт 1)
                await networkStream.WriteAsync(new byte[] { 1 }, 0, 1);
                AnsiConsole.MarkupLine("[green]auth:[/] success. starting download.");
            }
            catch
            {
                AnsiConsole.MarkupLine("[bold red]err:[/] incorrect_pass. access denied.");
                return; // Разрываем связь, ничего не отправляя
            }

            // 7. Прием файла
            using var cryptoStream = new CryptoStream(networkStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
            string savePath = Path.Combine(Environment.CurrentDirectory, "dl_" + meta!.name);

            try
            {
                await AnsiConsole.Progress()
                    .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new DownloadedColumn(), new TransferSpeedColumn())
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask("[grey]pulling:[/] ...");
                        task.MaxValue = meta.size;
                        using var fs = new System.IO.FileStream(savePath, FileMode.Create, FileAccess.Write);
                        byte[] buffer = new byte[BufferSize];
                        long total = 0;
                        while (total < meta.size)
                        {
                            int read = await cryptoStream.ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0) break;
                            await fs.WriteAsync(buffer, 0, read);
                            total += read;
                            task.Increment(read);
                        }
                    });
            }
            catch
            {
                if (System.IO.File.Exists(savePath)) System.IO.File.Delete(savePath);
                throw new Exception("decryption_failed_or_interrupted");
            }

            AnsiConsole.MarkupLine("[yellow]verifying_integrity...[/]");
            string downloadedHash = await CalculateHashAsync(savePath);
            if (downloadedHash == meta.hash) AnsiConsole.MarkupLine("[bold green]success:[/] verified.");
            else AnsiConsole.MarkupLine("[bold red]danger:[/] hash_mismatch.");
        }

        // Вспомогательный метод для гарантированного чтения N байт
        static async Task ReadFullAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset);
                if (read == 0) throw new Exception("unexpected_end_of_stream");
                offset += read;
            }
        }

        static async Task<string> CalculateHashAsync(string path)
        {
            using var sha = SHA256.Create();
            using var fs = new System.IO.FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
            byte[] hash = await sha.ComputeHashAsync(fs);
            return Convert.ToHexString(hash).ToLower();
        }

        // ТЕПЕРЬ СОЛЬ ПЕРЕДАЕТСЯ АРГУМЕНТОМ
        static (byte[] key, byte[] iv) DeriveKey(string password, byte[] salt)
        {
            // Используем переданную случайную соль вместо статической
            using var deriveBytes = new Rfc2898DeriveBytes(password, salt, 1000, HashAlgorithmName.SHA256);
            return (deriveBytes.GetBytes(32), deriveBytes.GetBytes(16));
        }

        static string? ReadInputWithEsc(string prompt)
        {
            AnsiConsole.Markup($"[orange1]{prompt}[/]");
            StringBuilder input = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape) return null;
                if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return input.ToString(); }
                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                {
                    input.Remove(input.Length - 1, 1);
                    Console.Write("\b \b");
                }
                else if (!char.IsControl(key.KeyChar)) { input.Append(key.KeyChar); Console.Write(key.KeyChar); }
            }
        }

        static string? SelectLocalIP()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ipList = host.AddressList
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork) // Только IPv4
                .Select(ip => ip.ToString())
                .ToList();

            if (ipList.Count == 0) throw new Exception("no_network_adapters_found");
            if (ipList.Count == 1) return ipList.First(); // Если IP один, не мучаем юзера

            // Если IP несколько, даем выбор
            AnsiConsole.MarkupLine("[yellow]multiple network interfaces detected.[/]");
            var selectedIp = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("select an [green]ip address[/] to broadcast:")
                    .AddChoices(ipList));

            return selectedIp;
        }

        static void ShowHelp()
        {
            AnsiConsole.MarkupLine("[lime]p2p_daemon v1.4.0[/]");
            AnsiConsole.MarkupLine("- security: randomized salts per session");
            AnsiConsole.MarkupLine("- protocol: handshake with auth confirmation");
            AnsiConsole.MarkupLine("- integrity: sha-256 pre/post verification");
            AnsiConsole.MarkupLine("- how to use: As push: Only ENTER pushing mode and only then paste/dragndrop the file.");
            AnsiConsole.MarkupLine("- how to use: As pull: DONT WRITE THE 8888 AT THE END (Its port, its dont needed). and try to write pass correctly ;)");
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("         or else the pusher will get his peer closed so be careful");
        }

        class FileMeta { public string name { get; set; } = ""; public long size { get; set; } public string hash { get; set; } = ""; }
    }
}
