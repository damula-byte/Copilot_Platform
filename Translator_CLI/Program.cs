using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using Middleware_console;

namespace TIA_Copilot_CLI
{
    class Program
    {

        private static TIA_V20 _tiaEngine = new TIA_V20(); // Đảm bảo bạn đã có class TIA_V20 trong project
        private static string _currentProjectName = "None";
        private static string _currentDeviceName = "None";
        private static string _currentDeviceType = "None";
        private static string _currentIp = "0.0.0.0";
        private static string _lastGeneratedFilePath = "";

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            AiEngine.InitializePaths();

            if (!File.Exists(AiEngine.PYTHON_EXE_PATH) || !File.Exists(AiEngine.PYTHON_SCRIPT_PATH))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("LỖI CẤU HÌNH: Không tìm thấy thư mục AI_Backend!");
                Console.ResetColor();
                return;
            }

            // MẤU CHỐT HYBRID Ở ĐÂY:
            // Nếu có tham số -> Chạy Headless rồi tắt
            if (args.Length > 0)
            {
                await RouteCommand(args);
            }
            // Nếu KHÔNG có tham số -> Mở chế độ Shell (REPL) xịn sò
            else
            {
                await RunInteractiveShell();
            }
        }

        // =================================================================
        // HÀM TẠO GIAO DIỆN SHELL RIÊNG BIỆT (REPL LOOP)
        // =================================================================
        static async Task RunInteractiveShell()
        {
            string userName = Environment.UserName;
            string appName = "TIACopilot";

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================");
            Console.WriteLine($" Chào mừng đến với {appName} Interactive Shell!");
            Console.WriteLine(" Gõ lệnh, hoặc bấm phím [ESC] / gõ 'exit' để thoát."); // ---> Đã cập nhật HDSD
            Console.WriteLine("==========================================================\n");
            Console.ResetColor();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{userName}@{appName}");
                Console.ResetColor();
                Console.Write(" > ");

                // ---> [SỬA Ở ĐÂY]: Dùng hàm mới thay cho Console.ReadLine()
                string input = ReadLineWithEscape();

                // Nếu input trả về null (Nghĩa là người dùng vừa đấm nút ESC)
                if (input == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[HỆ THỐNG] Đã nhận lệnh ESC. Tắt engine...");
                    Console.ResetColor();
                    break; // Phá vỡ vòng lặp, thoát app!
                }

                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.Trim().ToLower() == "exit") break;

                string[] cmdArgs = Regex.Matches(input, @"[\""].+?[\""]|[^ ]+")
                                        .Select(m => m.Value.Trim('"'))
                                        .ToArray();

                await RouteCommand(cmdArgs);
            }
        }

        // =================================================================
        // BỘ ĐỊNH TUYẾN LỆNH (ROUTER)
        // =================================================================
        static async Task RouteCommand(string[] args)
        {
            string command = args[0].ToLower();
            string sessionId = CommandHandler.DefaultSessionID;

            try
            {
                switch (command)
                {
                    case "chat":
                        string targetType = args.Length > 1 ? CommandHandler.GetBlockType(args[1]) : "AUTO";
                        string query = args.Length > 2 ? args[2] : "";
                        if (args.Length > 3) sessionId = args[3];

                        if (string.IsNullOrEmpty(query))
                        {
                            Console.WriteLine("LỖI: Bạn phải truyền câu lệnh yêu cầu (query).");
                            return;
                        }
                        await CommandHandler.HandleChatAsync(targetType, query, sessionId);
                        break;

                    case "load-tags":
                        string tagFile = args.Length > 1 ? args[1] : "";
                        await CommandHandler.HandleLoadTagsAsync(tagFile);
                        break;

                    case "load-spec":
                        string specFile = args.Length > 1 ? args[1] : "";
                        if (args.Length > 2) sessionId = args[2];
                        await CommandHandler.HandleLoadSpecAsync(specFile, sessionId);
                        break;

                    case "clear-data":
                        if (args.Length > 1) sessionId = args[1];
                        await CommandHandler.HandleClearDataAsync(sessionId);
                        break;

                    case "help":
                    default:
                        PrintHelp();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[SYSTEM CRASH] Lỗi thực thi lệnh: {ex.Message}");
                Console.ResetColor();
            }
        }

        public static void HandleTiaCommand(string[] args)
        {
            if (args.Length < 2)
            {
                PrintIcon("!", "LỖI CÚ PHÁP: Bạn cần nhập lệnh phụ cho 'tia'. (VD: tia connect, tia open...)", ConsoleColor.Yellow);
                return;
            }

            string action = args[1].ToLower();

            switch (action)
            {
                // 1. KẾT NỐI TIA ĐANG MỞ
                // Cú pháp: tia connect
                case "connect":
                    PrintIcon("i", "Đang tìm kiếm tiến trình TIA Portal...", ConsoleColor.Cyan);
                    if (_tiaEngine.ConnectToTIA())
                    {
                        _currentProjectName = _tiaEngine.GetProjectName();
                        PrintIcon("√", $"Đã kết nối thành công với dự án: {_currentProjectName}", ConsoleColor.Green);
                    }
                    else
                    {
                        PrintIcon("×", "Không tìm thấy TIA Portal nào đang chạy.", ConsoleColor.Red);
                    }
                    break;

                // 2. MỞ PROJECT CÓ SẴN (HEADLESS)
                // Cú pháp: tia open "C:\duong_dan\project.apxx"
                case "open":
                    if (args.Length < 3)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia open \"<Đường_dẫn_file_apxx>\"", ConsoleColor.Red);
                        return;
                    }
                    string openPath = args[2];
                    if (File.Exists(openPath))
                    {
                        PrintIcon("i", $"Đang mở project từ: {openPath}...", ConsoleColor.Cyan);
                        if (_tiaEngine.CreateTIAproject(openPath, "", false))
                        {
                            _currentProjectName = Path.GetFileNameWithoutExtension(openPath);
                            PrintIcon("√", $"Đã mở thành công: {_currentProjectName}", ConsoleColor.Green);
                        }
                    }
                    else PrintIcon("×", "Không tìm thấy file Project này!", ConsoleColor.Red);
                    break;

                // 3. TẠO PROJECT MỚI (HEADLESS)
                // Cú pháp: tia create "C:\duong_dan_luu" "Ten_Du_An"
                case "create":
                    if (args.Length < 4)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia create \"<Thư_mục_lưu>\" \"<Tên_Project>\"", ConsoleColor.Red);
                        return;
                    }
                    string cPath = args[2];
                    string cName = args[3];
                    PrintIcon("i", $"Đang tạo project mới [{cName}] tại [{cPath}]...", ConsoleColor.Cyan);

                    if (_tiaEngine.CreateTIAproject(cPath, cName, true))
                    {
                        _currentProjectName = cName;
                        PrintIcon("√", "Tạo Project thành công.", ConsoleColor.Green);
                    }
                    break;

                // 4. LƯU PROJECT
                // Cú pháp: tia save
                case "save":
                    PrintIcon("i", "Đang lưu dự án...", ConsoleColor.Cyan);
                    _tiaEngine.SaveProject();
                    PrintIcon("√", "Đã lưu an toàn.", ConsoleColor.Green);
                    break;

                // 5. ĐÓNG PROJECT
                // Cú pháp: tia close
                case "close":
                    PrintIcon("i", "Đang đóng TIA Portal...", ConsoleColor.Cyan);
                    _tiaEngine.CloseTIA();
                    _currentProjectName = "None";
                    _currentDeviceName = "None";
                    PrintIcon("√", "TIA Portal đã được đóng hoàn toàn.", ConsoleColor.DarkGray);
                    break;

                case "device":
                    if (args.Length < 5)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia device \"<Tên_PLC>\" \"<IP>\" \"<Mã_Order_hoặc_Tên_JSON>\"", ConsoleColor.Red);
                        return;
                    }
                    string devName = args[2];
                    string devIp = args[3];
                    string devTypeInfo = args[4];
                    string typeIdentifier = devTypeInfo; // Mặc định giả sử người dùng truyền Manual

                    // Nếu truyền tên (không có OrderNumber:), quét tìm trong file PlcCatalog.json
                    if (!devTypeInfo.StartsWith("OrderNumber:") && File.Exists("PlcCatalog.json"))
                    {
                        try
                        {
                            var catalog = JsonConvert.DeserializeObject<List<PlcCatalogItem>>(File.ReadAllText("PlcCatalog.json"));
                            var match = catalog.FirstOrDefault(x => x.Name.Equals(devTypeInfo, StringComparison.OrdinalIgnoreCase));
                            if (match != null)
                            {
                                typeIdentifier = match.GetTypeIdentifier();
                            }
                        }
                        catch { /* Bỏ qua lỗi đọc JSON nếu có */ }
                    }

                    try
                    {
                        PrintIcon("i", $"Đang tạo thiết bị [{devName}] (IP: {devIp}) với mã [{typeIdentifier}]...", ConsoleColor.Cyan);
                        _tiaEngine.CreateDev(devName, typeIdentifier, devIp, "");

                        // Cập nhật trạng thái mục tiêu
                        _currentDeviceName = devName;
                        _currentIp = devIp;
                        _currentDeviceType = typeIdentifier;

                        PrintIcon("√", $"Đã tạo PLC '{devName}' thành công và tự động chọn làm mục tiêu.", ConsoleColor.Green);
                    }
                    catch (Exception ex) { PrintIcon("×", $"Lỗi tạo thiết bị: {ex.Message}", ConsoleColor.Red); }
                    break;

                // 7. CHỌN PLC MỤC TIÊU
                // Cú pháp: tia choose "<Tên_PLC>"
                case "choose":
                    if (args.Length < 3)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia choose \"<Tên_PLC>\"", ConsoleColor.Red);
                        return;
                    }
                    string targetDev = args[2];
                    var devs = _tiaEngine.GetPlcList();

                    if (devs.Contains(targetDev, StringComparer.OrdinalIgnoreCase))
                    {
                        _currentDeviceName = devs.First(d => d.Equals(targetDev, StringComparison.OrdinalIgnoreCase));
                        _currentDeviceType = _tiaEngine.GetDeviceType(_currentDeviceName);
                        _currentIp = _tiaEngine.GetDeviceIp(_currentDeviceName);
                        PrintIcon("√", $"Đã khóa mục tiêu vào: {_currentDeviceName} ({_currentIp})", ConsoleColor.Green);
                    }
                    else
                    {
                        PrintIcon("×", $"Không tìm thấy '{targetDev}'. Danh sách PLC hiện có: {string.Join(", ", devs)}", ConsoleColor.Red);
                    }
                    break;

                // 8. CẤU HÌNH KẾT NỐI HMI - PLC
                // Cú pháp: tia hmi-conn "<HMI_IP>" "<PLC_IP>"
                case "hmi-conn":
                    if (args.Length < 4)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia hmi-conn \"<HMI_IP>\" \"<PLC_IP>\"", ConsoleColor.Red);
                        return;
                    }
                    string hmiIp = args[2];
                    string plcIp = args[3];
                    PrintIcon("i", $"Đang thiết lập kết nối WinCC Unified ({hmiIp}) và PLC ({plcIp})...", ConsoleColor.Cyan);
                    _tiaEngine.CreateUnifiedConnectionCombined("PC-System_1", hmiIp, plcIp, "HMI_PLC_Conn");
                    PrintIcon("√", "Đã cấu hình xong HMI Connection.", ConsoleColor.Green);
                    break;

                // 9. NẠP KHỐI LOGIC SCL (FB / FC / OB)
                // Cú pháp: tia fb "<Đường_dẫn_SCL>" (Nếu không truyền đường dẫn, sẽ tự động lấy file của AI vừa đẻ ra)
                case "fb":
                case "fc":
                case "ob":
                    string sclPath = args.Length > 2 ? args[2] : "";
                    TiaImportLogic(action.ToUpper(), sclPath);
                    break;

                // 10. NẠP TAGS CHO PLC TỪ FILE CSV
                // Cú pháp: tia tag-plc "<Đường_dẫn_CSV>"
                case "tag-plc":
                    if (args.Length < 3)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia tag-plc \"<Đường_dẫn_CSV>\"", ConsoleColor.Red);
                        return;
                    }
                    string plcTagPath = args[2];
                    if (File.Exists(plcTagPath))
                    {
                        PrintIcon("i", $"Đang nạp PLC Tags từ: {plcTagPath}...", ConsoleColor.Cyan);
                        _tiaEngine.ImportPlcTagsFromCsv(_currentDeviceName, plcTagPath);
                        PrintIcon("√", "Nạp PLC Tags thành công.", ConsoleColor.Green);
                    }
                    else PrintIcon("×", "Không tìm thấy file CSV!", ConsoleColor.Red);
                    break;

                // 11. NẠP TAGS CHO HMI (WINCC UNIFIED) TỪ FILE CSV
                // Cú pháp: tia tag-hmi "<Đường_dẫn_CSV>"
                case "tag-hmi":
                    if (args.Length < 3)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia tag-hmi \"<Đường_dẫn_CSV>\"", ConsoleColor.Red);
                        return;
                    }
                    string hmiTagPath = args[2];
                    if (File.Exists(hmiTagPath))
                    {
                        PrintIcon("i", $"Đang nạp HMI Tags từ: {hmiTagPath}...", ConsoleColor.Cyan);
                        _tiaEngine.ImportHmiTagsFromCsv("PC-System_1", hmiTagPath); // Mặc định đích là PC-System_1
                        PrintIcon("√", "Nạp HMI Tags thành công.", ConsoleColor.Green);
                    }
                    else PrintIcon("×", "Không tìm thấy file CSV!", ConsoleColor.Red);
                    break;

                // 12. VẼ MÀN HÌNH SCADA TỪ JSON
                // Cú pháp: tia draw "<Đường_dẫn_JSON>"
                case "draw":
                    if (args.Length < 3)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia draw \"<Đường_dẫn_JSON>\"", ConsoleColor.Red);
                        return;
                    }
                    string jsonPath = args[2];
                    if (File.Exists(jsonPath))
                    {
                        PrintIcon("i", $"Đang đọc cấu trúc đồ họa từ: {jsonPath}...", ConsoleColor.Cyan);
                        try
                        {
                            var data = JsonConvert.DeserializeObject<ScadaScreenModel>(File.ReadAllText(jsonPath));
                            // Mặc định thiết bị HMI Unified PC có tên là "PC-System_1"
                            _tiaEngine.CreateUnifiedScreen("PC-System_1", data.ScreenName);
                            // ValidateAndFixGraphics(data); // Bỏ comment nếu bạn đã định nghĩa hàm này
                            _tiaEngine.GenerateScadaScreenFromData("PC-System_1", data);
                            PrintIcon("√", $"Đã vẽ thành công màn hình: {data.ScreenName}", ConsoleColor.Green);
                        }
                        catch (Exception ex) { PrintIcon("×", $"Lỗi vẽ SCADA: {ex.Message}", ConsoleColor.Red); }
                    }
                    else PrintIcon("×", "Không tìm thấy file JSON SCADA!", ConsoleColor.Red);
                    break;

                // 13. NẠP ẢNH ĐỒ HỌA (HỖ TRỢ SINGLE FILE & BATCH FOLDER)
                // Cú pháp: tia img "<Đường_dẫn_File_Ảnh_HOẶC_Thư_Mục_Ảnh>"
                case "img":
                    if (args.Length < 3)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia img \"<Đường_dẫn_Ảnh_hoặc_Thư_Mục>\"", ConsoleColor.Red);
                        return;
                    }
                    string imgTarget = args[2];

                    if (File.Exists(imgTarget)) // Chế độ Single (Ảnh đơn)
                    {
                        PrintIcon("i", $"Đang nạp ảnh đơn: {Path.GetFileName(imgTarget)}...", ConsoleColor.Cyan);
                        _tiaEngine.AddPngToProjectGraphics(imgTarget, Path.GetFileNameWithoutExtension(imgTarget));
                        PrintIcon("√", "Đã nạp ảnh vào thư viện TIA.", ConsoleColor.Green);
                    }
                    else if (Directory.Exists(imgTarget)) // Chế độ Batch (Quét thư mục)
                    {
                        PrintIcon("i", $"Đang quét thư mục ảnh (Batch mode): {imgTarget}...", ConsoleColor.Cyan);
                        var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".svg" };
                        var files = Directory.GetFiles(imgTarget, "*.*").Where(f => validExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();

                        int count = 0;
                        foreach (var f in files)
                        {
                            _tiaEngine.AddPngToProjectGraphics(f, Path.GetFileNameWithoutExtension(f));
                            count++;
                        }
                        PrintIcon("√", $"Đã nạp hàng loạt {count} ảnh vào thư viện TIA.", ConsoleColor.Green);
                    }
                    else PrintIcon("×", "Đường dẫn không hợp lệ. Không phải File ảnh cũng không phải Thư mục.", ConsoleColor.Red);
                    break;

                // 14. XUẤT THÔNG SỐ MÀN HÌNH (EXPORT SYMBOL PATH)
                // Cú pháp: tia export "<Tên_Màn_Hình>"
                case "export":
                    if (args.Length < 3)
                    {
                        PrintIcon("×", "Thiếu tham số! Cú pháp: tia export \"<Tên_Màn_Hình>\"", ConsoleColor.Red);
                        return;
                    }
                    string scrName = args[2];
                    PrintIcon("i", $"Đang quét và trích xuất dữ liệu từ màn hình [{scrName}]...", ConsoleColor.Cyan);
                    _tiaEngine.ExportAllPathsFromScreen(_currentDeviceName, scrName, "");
                    PrintIcon("√", "Quét và xuất dữ liệu hoàn tất.", ConsoleColor.Green);
                    break;

                // 15. BIÊN DỊCH CHƯƠNG TRÌNH
                // Cú pháp: tia compile [hw/sw/both]
                case "compile":
                    string cMode = args.Length > 2 ? args[2].ToLower() : "both";
                    PrintIcon("i", $"Đang biên dịch {_currentDeviceName} (Chế độ: {cMode})...", ConsoleColor.Cyan);
                    _tiaEngine.CompileSpecific(_currentDeviceName, cMode == "hw" || cMode == "both", cMode == "sw" || cMode == "both");
                    PrintIcon("√", "Biên dịch hoàn tất.", ConsoleColor.Green);
                    break;

                // 16. ĐỔ CHƯƠNG TRÌNH XUỐNG PLC
                // Cú pháp: tia download [ID_hoặc_Tên_Card_Mạng]
                case "download":
                    string dlCard = SelectAdapter(args.Length > 2 ? string.Join(" ", args.Skip(2)) : "");
                    if (!string.IsNullOrEmpty(dlCard)) 
                    {
                        PrintIcon("i", $"Đang nạp chương trình xuống {_currentIp} qua card [{dlCard}]...", ConsoleColor.Cyan);
                        string res = _tiaEngine.DownloadToPLC(_currentDeviceName, _currentIp, dlCard);
                        Console.WriteLine(res);
                    }
                    break;

                // 17. CHUYỂN PLC SANG RUN
                // Cú pháp: tia run [ID_hoặc_Tên_Card_Mạng]
                case "run":
                    string rCard = SelectAdapter(args.Length > 2 ? string.Join(" ", args.Skip(2)) : "");
                    if (!string.IsNullOrEmpty(rCard)) 
                    {
                        PrintIcon("i", $"Đang gửi lệnh RUN tới {_currentDeviceName}...", ConsoleColor.Cyan);
                        string rMsg = _tiaEngine.ChangePlcState(_currentDeviceName, _currentIp, rCard, true);
                        PrintIcon("√", rMsg, ConsoleColor.Green);
                    }
                    break;

                // 18. CHUYỂN PLC SANG STOP
                // Cú pháp: tia stop [ID_hoặc_Tên_Card_Mạng]
                case "stop":
                    string sCard = SelectAdapter(args.Length > 2 ? string.Join(" ", args.Skip(2)) : "");
                    if (!string.IsNullOrEmpty(sCard)) 
                    {
                        PrintIcon("i", $"Đang gửi lệnh STOP tới {_currentDeviceName}...", ConsoleColor.Cyan);
                        string sMsg = _tiaEngine.ChangePlcState(_currentDeviceName, _currentIp, sCard, false);
                        PrintIcon("√", sMsg, ConsoleColor.Green);
                    }
                    break;

                // 19. KIỂM TRA TRẠNG THÁI ONLINE
                // Cú pháp: tia check [ID_hoặc_Tên_Card_Mạng]
                case "check":
                    string chCard = SelectAdapter(args.Length > 2 ? string.Join(" ", args.Skip(2)) : "");
                    if (!string.IsNullOrEmpty(chCard)) 
                    {
                        PrintIcon("i", $"Đang kiểm tra trạng thái Online của {_currentDeviceName}...", ConsoleColor.Cyan);
                        string status = _tiaEngine.GetPlcStatus(_currentDeviceName, chCard);
                        PrintIcon("√", status, ConsoleColor.Green);
                    }
                    break;

                default:
                    PrintIcon("×", $"Lệnh 'tia {action}' chưa được hỗ trợ trong Hiệp 1 hoặc sai cú pháp.", ConsoleColor.Red);
                    break;
            }
        }


        public static void PrintIcon(string icon, string msg, ConsoleColor c)
        {
            Console.ForegroundColor = c;
            Console.Write($"[{icon}] ");
            Console.ResetColor();
            Console.WriteLine(msg);
        }

        public static void TiaImportLogic(string blockType, string explicitPath)
        {
            PrintIcon("i", $"--- BẮT ĐẦU IMPORT {blockType} ---", ConsoleColor.Cyan);

            string path = explicitPath;

            // Nếu người dùng gõ lệnh cộc lốc (VD: "tia fb") mà không truyền path
            // Ta tự động đi tìm file do AI (FileFormatter) vừa lưu ra ở thư mục hiện tại
            if (string.IsNullOrEmpty(path))
            {
                // Lưu ý: Tên file này phải khớp với cách bạn đặt tên khi xuất file ở FileFormatter.cs
                // Mặc định chúng ta có thể quét tìm file .scl mới nhất trong thư mục làm việc:
                var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                var latestSclFile = directory.GetFiles("*.scl").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();

                if (latestSclFile != null)
                {
                    path = latestSclFile.FullName;
                    PrintIcon("i", $"Tự động nhặt file AI vừa sinh ra: {latestSclFile.Name}", ConsoleColor.DarkGray);
                }
            }

            if (File.Exists(path))
            {
                try
                {
                    string targetPlc = !string.IsNullOrEmpty(_currentDeviceName) && _currentDeviceName != "None" ?
                                       _currentDeviceName : _tiaEngine.GetPlcList().FirstOrDefault();

                    if (string.IsNullOrEmpty(targetPlc))
                    {
                        PrintIcon("×", "LỖI: Chưa có PLC nào trong Project để nạp code!", ConsoleColor.Red);
                    }
                    else
                    {
                        PrintIcon("i", $"Đang biên dịch và nạp file SCL vào PLC [{targetPlc}]...", ConsoleColor.Cyan);
                        _tiaEngine.CreateFBblockFromSource(targetPlc, path);
                        PrintIcon("√", $"Nạp {blockType} vào {targetPlc} thành công!", ConsoleColor.Green);
                    }
                }
                catch (Exception ex) { PrintIcon("×", $"Lỗi Import: {ex.Message}", ConsoleColor.Red); }
            }
            else
            {
                PrintIcon("×", "Lỗi: Không tìm thấy file SCL nào để nạp!", ConsoleColor.Red);
            }
        }

        private static string SelectAdapter(string inputArg = "")
        {
            var adapters = TIA_V20.GetSystemNetworkAdapters(); // Phải đảm bảo hàm này trả về List<string>

            if (adapters == null || adapters.Count == 0)
            {
                PrintIcon("×", "Không tìm thấy Card mạng (Network Adapter) nào trên máy tính.", ConsoleColor.Red);
                return null;
            }

            // Nếu người dùng CÓ truyền tham số (ID hoặc Tên Card)
            if (!string.IsNullOrWhiteSpace(inputArg))
            {
                // Thử parse xem người dùng gõ số ID (VD: 1, 2, 3) hay không
                if (int.TryParse(inputArg, out int index) && index > 0 && index <= adapters.Count)
                {
                    return adapters[index - 1]; // Trả về Card mạng tương ứng với số thứ tự
                }
                
                // Nếu người dùng gõ một phần tên card (VD: "Intel" hoặc "PLCSIM")
                var match = adapters.FirstOrDefault(a => a.Contains(inputArg, StringComparison.OrdinalIgnoreCase));
                if (match != null) 
                {
                    return match;
                }

                PrintIcon("×", $"Không tìm thấy card mạng nào khớp với từ khóa '{inputArg}'.", ConsoleColor.Red);
            }

            // Nếu người dùng KHÔNG truyền tham số, hoặc truyền sai -> In ra danh sách bảng
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n---------------------------------------------------------");
            Console.WriteLine(" ID | NETWORK INTERFACE (PG/PC) ");
            Console.WriteLine("---------------------------------------------------------");
            Console.ResetColor();
            
            for (int i = 0; i < adapters.Count; i++)
            {
                Console.WriteLine($" {i + 1,-2} | {adapters[i]}");
            }
            Console.WriteLine("---------------------------------------------------------");
            
            PrintIcon("!", "LỖI: Bạn chưa đính kèm Card mạng. Vui lòng thêm ID hoặc Tên Card vào sau lệnh.", ConsoleColor.Yellow);
            PrintIcon("i", "Ví dụ: tia download 1 (Nạp bằng card số 1)", ConsoleColor.DarkGray);
            PrintIcon("i", "Ví dụ: tia run \"PLCSIM\" (Chạy mô phỏng)", ConsoleColor.DarkGray);
            
            return null; // Trả về null để hủy thao tác Download/Run/Stop
        }

        static string ReadLineWithEscape()
        {
            StringBuilder input = new StringBuilder();
            while (true)
            {
                // Đọc 1 phím ấn vào, intercept = true nghĩa là không tự động in ra màn hình
                var keyInfo = Console.ReadKey(intercept: true);

                // Nếu là nút ESC -> Trả về null (Báo hiệu tự hủy)
                if (keyInfo.Key == ConsoleKey.Escape)
                {
                    return null;
                }
                // Nếu là nút Enter -> Kết thúc nhập lệnh
                else if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine(); // Xuống dòng
                    return input.ToString();
                }
                // Nếu là nút Xóa (Backspace) -> Xóa lùi lại 1 ký tự
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (input.Length > 0)
                    {
                        input.Length--; // Xóa ký tự cuối trong bộ nhớ
                        Console.Write("\b \b"); // Xóa ký tự cuối trên màn hình đen
                    }
                }
                // Các phím bình thường (Chữ, số, ký tự đặc biệt...)
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    input.Append(keyInfo.KeyChar);
                    Console.Write(keyInfo.KeyChar); // Tự in ra màn hình
                }
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("\nDanh sách lệnh:");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[AI MODULE]");
            Console.ResetColor();

            Console.WriteLine("  load-tags \"<Đường_dẫn_File_Excel/CSV>\"");
            Console.WriteLine("  chat <Type> \"<Query>\" [SessionID]");
            Console.WriteLine("  load-spec \"<Đường_dẫn_File_Spec.txt>\" [SessionID]");
            Console.WriteLine("  clear-data [SessionID]");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[TIA MODULE] - Tương tác với TIA Portal V20");
            Console.ResetColor();

            Console.WriteLine("\n[1-5: QUẢN LÝ DỰ ÁN & KẾT NỐI]");
            Console.WriteLine("  tia connect      : Kết nối TIA Portal đang chạy");
            Console.WriteLine("  tia open         : Mở project từ file (.apxx)");
            Console.WriteLine("  tia create       : Tạo project mới");
            Console.WriteLine("  tia save         : Lưu project hiện tại");
            Console.WriteLine("  tia close        : Đóng TIA Portal");

            Console.WriteLine("\n[6-8: THIẾT BỊ & CẤU HÌNH]");
            Console.WriteLine("  tia device       : Tạo PLC mới (JSON/Manual)");
            Console.WriteLine("  tia choose       : Chọn PLC mục tiêu để thao tác");
            Console.WriteLine("  tia hmi-conn     : Thiết lập kết nối HMI-PLC");

            Console.WriteLine("\n[9-13: LẬP TRÌNH & DỮ LIỆU]");
            Console.WriteLine("  tia fb / fc / ob : Import khối code SCL");
            Console.WriteLine("  tia tag-plc      : Import PLC Tags từ CSV");
            Console.WriteLine("  tia tag-hmi      : Import HMI Tags từ CSV");

            Console.WriteLine("\n[14-16: WINCC UNIFIED & SCADA]");
            Console.WriteLine("  tia draw         : Vẽ màn hình từ JSON");
            Console.WriteLine("  tia img          : Import ảnh vào thư viện");
            Console.WriteLine("  tia export       : Xuất Symbol Path từ màn hình (DEV ONLY)");

            Console.WriteLine("\n[17-21: VẬN HÀNH & ONLINE]");
            Console.WriteLine("  tia compile      : Biên dịch (hw/sw/both)");
            Console.WriteLine("  tia download     : Đổ chương trình xuống PLC");
            Console.WriteLine("  tia run          : Chuyển PLC sang RUN");
            Console.WriteLine("  tia stop         : Chuyển PLC sang STOP");
            Console.WriteLine("  tia check        : Kiểm tra kết nối Online");
            Console.WriteLine("  exit\n");
        }
    }
}