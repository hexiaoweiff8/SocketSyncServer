using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ProtoBuf;

namespace WindowsFormsApplication1
{
    public partial class SocketServer : Form
    {

        ///// <summary>
        ///// 最大丢包次数
        ///// </summary>
        //private const int MaxLossTime = 5;

        ///// <summary>
        ///// 帧编号
        ///// </summary>
        //private static long FrameNum
        //{
        //    get { return frameNum++; }
        //}


        //private static int MemberId
        //{
        //    get
        //    {
        //        return memberId++;
        //    }
        //}

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

        /// <summary>
        /// 数据缓冲区
        /// </summary>
        private byte[] buffer = new byte[0];

        ///// <summary>
        ///// 未处理数据
        ///// </summary>
        //private byte[] unhandleBuffer = new byte[0];

        /// <summary>
        /// 数据列表字典
        /// 保存每个人的ID以及发送的数据
        /// </summary>
        private List<MsgHead> dataList = new List<MsgHead>();

        /// <summary>
        /// 匹配列表
        /// </summary>
        private Dictionary<int, int> matchDic = new Dictionary<int, int>();

        /// <summary>
        /// 未匹配列表
        /// </summary>
        private List<int> unmatchList = new List<int>();

        ///// <summary>
        ///// Id匹配EndPoint
        ///// </summary>
        //private Dictionary<int, EndPoint> idMapEndPointDic = new Dictionary<int, EndPoint>();

        ///// <summary>
        ///// Id匹配地址与端口
        ///// </summary>
        //private Dictionary<int, string> idMapAddressAndPort = new Dictionary<int, string>();

        ///// <summary>
        ///// ID匹配丢包次数
        ///// </summary>
        //private Dictionary<int, int> idMapNetLossTime = new Dictionary<int, int>();

        /// <summary>
        /// ID匹配Socket链接
        /// </summary>
        private Dictionary<int, Socket> idMapSocket = new Dictionary<int, Socket>();

        /// <summary>
        /// ip地址对应
        /// </summary>
        private Dictionary<string, Socket> ipStringMapSocket = new Dictionary<string, Socket>();

        /// <summary>
        /// 匹配删除列表
        /// </summary>
        private List<int> matchDelList = new List<int>(); 


        ///// <summary>
        ///// 线程列表
        ///// </summary>
        //private List<Thread> threadList = new List<Thread>();

        /// <summary>
        /// 本地套接字
        /// </summary>
        private Socket socket;

        // ------------------------------事件--------------------------------

        public SocketServer()
        {
            InitializeComponent();
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

        private void SocketServer_Load(object sender, EventArgs e)
        {
            StartServer();
        }

        /// <summary>
        /// 同步数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void timer1_Tick(object sender, EventArgs e)
        {
            ReadData();
        }

        private void SocketServer_FormClosed(object sender, FormClosedEventArgs e)
        {
            // 断开链接
            socket.Close();
            // TODO 断开其他链接

            //threadAcceptSocket.Abort();
        }

        // ------------------------------事件--------------------------------

        // ------------------------------功能--------------------------------
        /// <summary>
        /// 启动服务器
        /// </summary>
        private void StartServer()
        {
            // 定义侦听端口
            var local = IPAddress.Parse("127.0.0.1");
            var iep = new IPEndPoint(local, 6000);
            // 定义套接字类型
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // 连接
            socket.Bind(iep);
            // 开始侦听
            socket.Listen(20);
            // 控制台输出侦听状态
            AddLog("开始接收链接");

            AsyncCallback acceptCallback = null;
            acceptCallback = (ayResult) =>
            {
                //try
                //{
                var aySocket = (Socket) ayResult.AsyncState;
                var clientSocket = aySocket.EndAccept(ayResult);
                // 判断是否为重新链接, 如果是则关闭其之前的链接
                var ipStr = ((IPEndPoint) clientSocket.RemoteEndPoint).Address.ToString();
                if (!ipStringMapSocket.ContainsKey(ipStr))
                {
                    ipStringMapSocket.Add(ipStr, clientSocket);
                }
                else if (ipStringMapSocket[ipStr].Connected)
                {
                    ipStringMapSocket[ipStr].Shutdown(SocketShutdown.Both);
                    ipStringMapSocket[ipStr].Close();
                    ipStringMapSocket.Remove(ipStr);
                    ipStringMapSocket.Add(ipStr, clientSocket);
                }
                else
                {
                    ipStringMapSocket.Remove(ipStr);
                }
                // 初始化数据缓冲区
                buffer = new byte[buffSize];

                AddLog("收到链接");
                // TODO 链接匹配到ID

                AsyncCallback callback = null;
                callback = (ia) =>
                {
                    try
                    {
                        // 处理数据
                        var readSocket = (Socket) ia.AsyncState;
                        if (readSocket.Connected)
                        {
                            var dataLength = readSocket.EndReceive(ia);

                            if (dataLength > 0)
                            {
                                // 数据放入缓存区
                                // 解析数据head
                                if (ByteUtils.CouldRead(buffer))
                                {
                                    var dataBody = ByteUtils.ReadMsg(ref buffer);
                                    var head = GetHead(dataBody, dataBody.Length);
                                    var userId = head.userId;
                                    // 匹配ID与Socket
                                    if (!idMapSocket.ContainsKey(userId))
                                    {
                                        idMapSocket.Add(userId, readSocket);
                                    }
                                    // 压入数据到待处理列表
                                    PushData(head);
                                }
                            }
                            else
                            {
                                // 断线
                                readSocket.Shutdown(SocketShutdown.Both);
                                readSocket.Close();
                            }

                            AddLog("收到" + dataLength + "长度数据");
                            // 初始化数据缓冲区
                            buffer = new byte[buffSize];
                            // 继续接收数据
                            readSocket.BeginReceive(buffer, 0, buffSize, SocketFlags.None, callback, readSocket);
                        }
                    }
                    catch (Exception e)
                    {
                        AddLog(e.Message);
                    }

                };
                // 开始接收数据
                clientSocket.BeginReceive(buffer, 0, buffSize, SocketFlags.None, callback, clientSocket);
                // 继续接收链接
                socket.BeginAccept(acceptCallback, socket);
                //}
                //catch(Exception e)
                //{
                //    AddLog(e.Message);
                //}

            };
            //开始接收链接
            socket.BeginAccept(acceptCallback, socket);
        }

        /// <summary>
        /// 获取数据头消息
        /// </summary>
        /// <param name="data"></param>
        /// <param name="dataLength"></param>
        /// <returns></returns>
        private MsgHead GetHead(byte[] data, int dataLength)
        {
            MsgHead result = null;

            var stream = new MemoryStream(ByteUtils.GetSubBytes(data, 0, dataLength));
            // 解析为类
            result = ProtoBuf.Serializer.Deserialize<MsgHead>(stream);

            return result;
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        private void ReadData()
        {
            // 循环每个人的数据
            foreach (var head in dataList)
            {
                // 客户端UserID
                var userId = head.userId;
                // 检查是否在匹配列表里
                if (!matchDic.ContainsKey(userId))
                {
                    // 匹配
                    // 检查未匹配列表中是否有单位
                    if (unmatchList.Count > 0)
                    {
                        var matchUserId = unmatchList[0];
                        if (matchUserId != userId)
                        {
                            // 加入匹配列表
                            unmatchList.RemoveAt(0);
                            matchDic.Add(matchUserId, userId);
                            matchDic.Add(userId, matchUserId);
                        }
                    }
                    else
                    {
                        // 加入为匹配列表
                        unmatchList.Add(userId);
                    }
                }

                // 解析数据
                while (ByteUtils.CouldRead(head.body))
                {
                    // 判断数据类型
                    switch (head.msgId)
                    {
                        case 1:
                        {
                            // 操作数据
                            var opData = ByteUtils.ReadMsg(ref head.body);
                            var stream = new MemoryStream(opData);
                            // 解析为类
                            var opMsg = ProtoBuf.Serializer.Deserialize<MsgOptional>(stream);
                            AddLog(opMsg.OpPosX + "," + opMsg.OpPosY + "," + opMsg.OpPosZ + ", OpType:" + opMsg.OpType +
                                   ", OpParams:" + opMsg.OpParams);
                            break;
                        }
                        case 2:
                        {
                            while (ByteUtils.CouldRead(head.body))
                            {
                                // 操作数据
                                var strData = ByteUtils.ReadMsg(ref head.body);
                                // 解析为字符串 字符集: UTF8
                                var str = Encoding.UTF8.GetString(strData);
                                AddLog(str);
                            }
                            break;
                        }
                    }
                }

                // TODO 转发数据
                // 判断是否已匹配, 如果匹配则转发数据, 否则不转发
                if (matchDic.ContainsKey(userId))
                {
                    // 转发数据
                    // 获取匹配的Id
                    var matchUserId = matchDic[userId];
                    // 该Id对应的Socket
                    var matchSocket = idMapSocket[matchUserId];
                    // 打包数据发送
                    SendMsg(matchSocket, Serialize(head));
                }
                else
                {
                    // 没匹配不转发, 输出log
                    AddLog("没有匹配不转发, userId:" + userId);
                }
            }
            // TODO 检查断线情况
            foreach (var kv in idMapSocket)
            {
                if (!kv.Value.Connected)
                {
                    // 断开
                    matchDelList.Add(kv.Key);
                }
            }
            // 删除匹配
            foreach (var delMatch in matchDelList)
            {
                if (!matchDic.ContainsKey(delMatch))
                {
                    continue;
                }
                var matchId = matchDic[delMatch];
                matchDic.Remove(matchId);
                matchDic.Remove(delMatch);
                // 链接关闭
                idMapSocket[matchId].Close();
                idMapSocket.Remove(matchId);
            }
            // 清空删除列表
            matchDelList.Clear();

            // 清空数据
            dataList.Clear();

            // 显示log
            if (!string.IsNullOrEmpty(log))
            {
                TxtLog.Text += log;
                log = "";
            }
        }

        /// <summary>
        /// 数据压入缓冲
        /// </summary>
        private void PushData(MsgHead head)
        {
            if (head == null)
            {
                return;
            }
            dataList.Add(head);
        }

        /// <summary>
        /// 打包数据
        /// </summary>
        /// <param name="packageData">被包装数据</param>
        /// <param name="uId">用户Id</param>
        /// <param name="msgId">数据Id</param>
        /// <returns>打包后的数据</returns>
        private byte[] PackageData(byte[] packageData, int uId, int msgId)
        {
            byte[] result = null;

            // 将数据打包放入MsgHead的body中
            var dataHead = new MsgHead()
            {
                msgId = msgId,
                userId = uId,
                body = SocketManager.AddDataHead(packageData),
            };
            var stream = new MemoryStream();
            Serializer.Serialize(stream, dataHead);
            result = stream.ToArray();

            return result;
        }

        ///// <summary>
        ///// 数据压入缓冲
        ///// </summary>
        ///// <param name="memId">对应单位的数据</param>
        ///// <param name="data">被缓冲数据</param>
        ///// <param name="length">数据长度</param>
        //private void PushData(int memId, byte[] data, int length)
        //{
        //    if (data == null || data.Length == 0)
        //    {
        //        return;
        //    }
        //    if (dataListDic.ContainsKey(memId))
        //    {
        //        dataListDic[memId] = ByteUtils.ConnectByte(dataListDic[memId], data, 0, length);
        //    }
        //    else
        //    {
        //        dataListDic.Add(memId, ByteUtils.GetSubBytes(data, 0, length));
        //    }
        //}

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
                AddLog("数据: " + memId + "," + dataStr);
            }
        }

        ///// <summary>
        ///// 是否为获取数据头
        ///// </summary>
        ///// <param name="buffer">被解析数据</param>
        ///// <returns>是否为获取数据头</returns>
        //private bool IsGetHead(byte[] buffer)
        //{
        //    if (!ByteUtils.CouldRead(buffer))
        //    {
        //        return false;
        //    }

        //    var result = false;

        //    var data = ByteUtils.ReadMsg(ref buffer);
        //    var dataStr = Encoding.UTF8.GetString(data);
        //    if (dataStr.Equals("GetHead"))
        //    {
        //        result = true;
        //    }
        //    return result;
        //}

        ///// <summary>
        ///// 发送数据头
        ///// </summary>
        //private void SendHead(EndPoint point)
        //{
        //    // 重新分配用户ID
        //    var memId = MemberId;
        //    // 发送ID
        //    var sendData = SocketManager.AddDataHead(Encoding.UTF8.GetBytes("head" + memId));

        //    AddLog("获取数据头. 生成数据头:" + memId);
        //    // 将新Id发送到目标
        //    socket.SendTo(sendData, point);
        //    idMapEndPointDic.Add(memId, point);
        //    // 匹配目标
        //    if (unmatchList.Count > 0)
        //    {
        //        // 进行匹配
        //        var matchMemberId = unmatchList[0];
        //        unmatchList.RemoveAt(0);
        //        matchDic.Add(matchMemberId, memId);
        //        matchDic.Add(memId, matchMemberId);
        //    }
        //    else
        //    {
        //        // 放入匹配列表等待匹配
        //        unmatchList.Add(memId);
        //    }
        //}


        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="beSendSocket"></param>
        /// <param name="msg">数据msg</param>
        /// <returns></returns>
        public bool SendMsg(Socket beSendSocket, byte[] msg)
        {
            if (beSendSocket == null || !beSendSocket.Connected)
            {
                AddLog("请先链接再发送消息");
                return false;
            }
            if (msg == null || msg.Length == 0)
            {
                AddLog("数据为空");
                return false;
            }

            try
            {
                // 数据格式化
                msg = SocketManager.AddDataHead(msg);
                var asyncSend = beSendSocket.BeginSend(msg, 0, msg.Length, SocketFlags.None, (ayResult) =>
                {
                    AddLog("发送成功");
                }, beSendSocket);
                if (asyncSend == null)
                {
                    AddLog("发送失败异步发送等待为空");
                    return false;
                }
                if (asyncSend.AsyncWaitHandle.WaitOne(5000, true))
                {
                    return true;
                }
                beSendSocket.EndSend(asyncSend);
                AddLog("发送失败" + asyncSend);
            }
            catch (Exception e)
            {
                AddLog("发送失败:" + e.Message);
            }

            return false;
        }


        // 将消息序列化为二进制的方法
        // < param name="model">要序列化的对象< /param>
        private byte[] Serialize<T>(T model)
        {
            try
            {
                //涉及格式转换，需要用到流，将二进制序列化到流中
                using (MemoryStream ms = new MemoryStream())
                {
                    //使用ProtoBuf工具的序列化方法
                    ProtoBuf.Serializer.Serialize<T>(ms, model);
                    //定义二级制数组，保存序列化后的结果
                    byte[] result = new byte[ms.Length];
                    //将流的位置设为0，起始点
                    ms.Position = 0;
                    //将流中的内容读取到二进制数组中
                    ms.Read(result, 0, result.Length);
                    return result;
                }
            }
            catch (Exception ex)
            {
                AddLog("序列化失败: " + ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// 添加log
        /// </summary>
        /// <param name="msg"></param>
        private void AddLog(string msg)
        {
            log += msg + "\r\n";
        }
    }
}

