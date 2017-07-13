using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WindowsFormsApplication1
{
    public partial class SocketServer : Form
    {

        /// <summary>
        /// 最大丢包次数
        /// </summary>
        private const int MaxLossTime = 5;

        /// <summary>
        /// 帧编号
        /// </summary>
        private static long FrameNum
        {
            get { return frameNum++; }
        }


        private static int MemberId
        {
            get
            {
                return memberId++;
            }
        }

        /// <summary>
        /// 数据列表字典
        /// 保存每个人的ID以及发送的数据
        /// </summary>
        private Dictionary<int, byte[]> dataListDic = new Dictionary<int, byte[]>();

        ///// <summary>
        ///// socket列表
        ///// 保存每个人的Socket对象
        ///// </summary>
        //private List<long> idDic = new List<long>();

        /// <summary>
        /// 匹配列表
        /// </summary>
        private Dictionary<int, int> matchDic = new Dictionary<int, int>();

        /// <summary>
        /// 未匹配列表
        /// </summary>
        private List<int> unmatchList = new List<int>();

        /// <summary>
        /// Id匹配EndPoint
        /// </summary>
        private Dictionary<int, EndPoint> idMapEndPointDic = new Dictionary<int, EndPoint>();

        ///// <summary>
        ///// Id匹配地址与端口
        ///// </summary>
        //private Dictionary<int, string> idMapAddressAndPort = new Dictionary<int, string>();

        /// <summary>
        /// ID匹配丢包次数
        /// </summary>
        private Dictionary<int, int> idMapNetLossTime = new Dictionary<int, int>();

        /// <summary>
        /// 匹配删除列表
        /// </summary>
        private List<int> matchDelList = new List<int>(); 

        /// <summary>
        /// 生成线程
        /// </summary>
        private Thread threadAcceptSocket;

        ///// <summary>
        ///// 线程列表
        ///// </summary>
        //private List<Thread> threadList = new List<Thread>();

        /// <summary>
        /// 本地套接字
        /// </summary>
        private Socket socket;

        /// <summary>
        /// 缓冲区
        /// </summary>
        //private byte[] buffer = new byte[0];

        /// <summary>
        /// log
        /// </summary>
        private string log = "";

        /// <summary>
        /// 帧编号
        /// </summary>
        private static long frameNum = 0;

        /// <summary>
        /// 单位数据
        /// </summary>
        private static int memberId = 1024;

        /// <summary>
        /// 数据缓冲长度
        /// 16k
        /// </summary>
        private int buffSize = 16384;


        public SocketServer()
        {
            InitializeComponent();
        }

        private void SocketServer_Load(object sender, EventArgs e)
        {
            //定义侦听端口
            var ipEnd = new IPEndPoint(IPAddress.Any, 6000);
            //定义套接字类型
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            //连接
            socket.Bind(ipEnd);
            //开始侦听
            //socket.Listen(0);
            //控制台输出侦听状态
            TxtLog.Text += "开始接收链接\r\n";
            byte[] receiveBuffer = new byte[buffSize];
            //一旦接受连接，创建一个客户端
            threadAcceptSocket = new Thread(new ThreadStart(() =>
            {
                while (true)
                {
                    try
                    {
                        Thread.Sleep(20);
                        //用来保存发送方的ip和端口号
                        EndPoint point = new IPEndPoint(IPAddress.Any, 0);
                        //接收数据报
                        var receiveLength = socket.ReceiveFrom(receiveBuffer, ref point);
                        // 查看对应表中是否有该数据
                        // 检查数据
                        // 如果是请求head数据则sendhead数据
                        if (IsGetHead(receiveBuffer))
                        {
                            // 发送数据头
                            SendHead(point);
                        }
                        else
                        {
                            // 获取数据头
                            var data = ByteUtils.ReadMsg(ref receiveBuffer);
                            var memId = Convert.ToInt32(Encoding.UTF8.GetString(data));
                            log += "已有数据头:" + memId + ":" + receiveLength + " \r\n";
                            PushData(Convert.ToInt32(memId), receiveBuffer, receiveLength - data.Length - 4);
                            // 更新断线计数
                            if (idMapNetLossTime.ContainsKey(memId))
                            {
                                idMapNetLossTime[memId] = 0;
                            }
                            else
                            {
                                idMapNetLossTime.Add(memId, 0);
                            }
                        }
                        //var memId = MemberId;
                        // 将地址信息对应到ID, 并生成ID
                        //idMapEndPointDic.Add(memId, point);
                        // 数据放入缓冲
                        //PushData(memId, buffer, receiveLength);
                        log += receiveLength + ":" + receiveBuffer + "--\r\n";
                    }
                    catch (Exception e2)
                    {
                        Console.WriteLine(e2.Message);
                    }
                    
                }
            }));
           threadAcceptSocket.Start();
            
        }

        /// <summary>
        /// 同步数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void timer1_Tick(object sender, EventArgs e)
        {
            // 处理数据
            foreach (var key in dataListDic.Keys)
            {
                var buffer = dataListDic[key];
                AnalysisData(buffer, key);
            }

            var tmpFrameNum = FrameNum;
            // 同步数据
            foreach (var item in matchDic)
            {
                var key = item.Key;
                var value = item.Value;
                //var targetSocket = item.Value;
                var dataArray = dataListDic.ContainsKey(key) ? dataListDic[key] : new byte[0];
                // 封装帧编号
                var frameNumData = Encoding.UTF8.GetBytes("" + tmpFrameNum);
                frameNumData = SocketManager.AddDataHead(frameNumData);
                dataArray = ByteUtils.ConnectByte(frameNumData, dataArray, 0, dataArray.Length);
                log += "同步数据" + dataArray.Length + "," + dataArray;
                socket.SendTo(dataArray, idMapEndPointDic[value]);

                // 丢包记录
                if (idMapNetLossTime.ContainsKey(key))
                {
                    idMapNetLossTime[key]++;
                    // 如果丢包次数大于上限则抛出断线
                    if (idMapNetLossTime[key] >= MaxLossTime)
                    {
                        // 断开匹配
                        matchDelList.Add(key);
                        matchDelList.Add(value);
                        log += key + "链接断开:" + key + ":" + value + "\r\n";
                    }
                }
                else
                {
                    idMapNetLossTime.Add(key, 1);
                }
            }
            // 删除匹配
            if (matchDelList.Count > 0)
            {
                foreach (var key in matchDelList)
                {
                    matchDic.Remove(key);
                }
                matchDelList.Clear();
            }
            // TODO 缓存数据
            dataListDic.Clear();
            if (!string.IsNullOrEmpty(log))
            {
                TxtLog.Text += log + "\r\n";
                log = "";
            }
        }

        private void SocketServer_FormClosed(object sender, FormClosedEventArgs e)
        {
            // 停止线程
            socket.Close();
            threadAcceptSocket.Abort();
        }

        /// <summary>
        /// 数据压入缓冲
        /// </summary>
        /// <param name="memId">对应单位的数据</param>
        /// <param name="data">被缓冲数据</param>
        /// <param name="length">数据长度</param>
        private void PushData(int memId, byte[] data, int length)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }
            if (dataListDic.ContainsKey(memId))
            {
                dataListDic[memId] = ByteUtils.ConnectByte(dataListDic[memId], data, 0, length);
            }
            else
            {
                dataListDic.Add(memId, ByteUtils.GetSubBytes(data, 0, length));
            }
        }

        /// <summary>
        /// 解析数据
        /// </summary>
        /// <param name="buffer">被解析数据</param>
        /// <param name="memId">所属用户ID</param>
        private void AnalysisData(byte[] buffer, int memId)
        {
            while (ByteUtils.CouldRead(buffer))
            {
                var data = ByteUtils.ReadMsg(ref buffer);
                var dataStr = Encoding.UTF8.GetString(data);
                log += "数据: " + memId + "," + dataStr + "--\r\n";
            }
        }

        /// <summary>
        /// 是否为获取数据头
        /// </summary>
        /// <param name="buffer">被解析数据</param>
        /// <returns>是否为获取数据头</returns>
        private bool IsGetHead(byte[] buffer)
        {
            if (!ByteUtils.CouldRead(buffer))
            {
                return false;
            }

            var result = false;

            var data = ByteUtils.ReadMsg(ref buffer);
            var dataStr = Encoding.UTF8.GetString(data);
            if (dataStr.Equals("GetHead"))
            {
                result = true;
            }
            return result;
        }

        /// <summary>
        /// 发送数据头
        /// </summary>
        private void SendHead(EndPoint point)
        {
            // 重新分配用户ID
            var memId = MemberId;
            // 发送ID
            var sendData = SocketManager.AddDataHead(Encoding.UTF8.GetBytes("head" + memId));

            log += "获取数据头. 生成数据头:" + memId;
            // 将新Id发送到目标
            socket.SendTo(sendData, point);
            idMapEndPointDic.Add(memId, point);
            // 匹配目标
            if (unmatchList.Count > 0)
            {
                // 进行匹配
                var matchMemberId = unmatchList[0];
                unmatchList.RemoveAt(0);
                matchDic.Add(matchMemberId, memId);
                matchDic.Add(memId, matchMemberId);
            }
            else
            {
                // 放入匹配列表等待匹配
                unmatchList.Add(memId);
            }
        }

        private void TxtLog_TextChanged(object sender, EventArgs e)
        {
            // 设置日志显示在最底端
            TxtLog.SelectionStart = TxtLog.Text.Length;
            TxtLog.ScrollToCaret(); 
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            // 清空日志
            TxtLog.Text = "";
        }
    }
}

