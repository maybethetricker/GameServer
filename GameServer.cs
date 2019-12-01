using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using MySql.Data;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Collections;
using System.Reflection;
using System.Threading;


namespace ConsoleApp1
{
    public class Conn
    {
        public const int BUFFER_SIZE = 1024;
        public Socket socket;
        public bool isUse = false;
        public byte[] readBuff = new byte[BUFFER_SIZE];
        public int buffCount = 0;
        public byte[] lenBytes = new byte[sizeof(UInt32)];
        public Int32 msgLength = 0;
        public long lastTickTime = long.MinValue;
        public Player player;
        public Conn()
        {
            readBuff = new byte[BUFFER_SIZE];
        }
        public void Init(Socket socket)
        {
            this.socket = socket;
            isUse = true;
            buffCount = 0;
            lastTickTime = Sys.GetTimeStamp();

        }
        public int BuffRemain()
        {
            return BUFFER_SIZE - buffCount;
        }
        public string GetAdress()
        {
            if (!isUse)
                return "Can't Get Adress";
            return socket.RemoteEndPoint.ToString();
        }
        public void Close()
        {
            if (!isUse)
                return;
            if (player != null)
            {
                player.Logout();
                return;
            }
            Console.WriteLine("[Close Link] ");
            try
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("Close Link error:" + e.Message);
            }
            isUse = false;

        }
        public void Send(ProtocolBase protocol)
        {
            ServNet.instance.Send(this, protocol);
        }
    }
    [Serializable]
    public class PlayerData
    {
        public int score = 0;
        public PlayerData()
        {
            score = 0;
        }
    }
    public class PlayerTempData
    {
        public enum Status
        {
            None,
            Fight,
        }
        public PlayerTempData()
        {
            status = Status.None;
        }
        public Status status;
        public Player enemy;
    }


    public class Player
    {
        public string id;
        public Conn conn;    //数据
        public PlayerData data;
        public PlayerTempData tempData;
        public Player(string id, Conn conn)
        {
            this.id = id;
            this.conn = conn;
            tempData = new PlayerTempData();
        }
        public void Send(ProtocolBase proto)
        {
            if (conn == null)
                return;
            ServNet.instance.Send(conn, proto);

        }
        public static bool KickOff(string id, ProtocolBase proto)
        {
            Conn[] conns = ServNet.instance.conns;
            for (int i = 0; i < conns.Length; i++)
            {
                if (conns[i] == null)
                    continue;
                if (!conns[i].isUse)
                    continue;
                if (conns[i].player == null)
                    continue;
                if (conns[i].player.id == id)
                {
                    lock (conns[i].player)
                    {
                        if (proto != null)
                            conns[i].player.Send(proto);
                        return conns[i].player.Logout();
                    }
                }
            }
            return true;
        }
        public bool Logout()
        {
            ServNet.instance.handlePlayerEvent.OnLogout(this);
            if (!DataMgr.instance.SavePlayer(this))
                return false;
            conn.player = null;
            conn.Close();
            return true;
        }
    }
    public class HandlePlayerEvent
    {
        public void OnLogin(Player player)
        {
        }
        public void OnLogout(Player player)
        {
	    if(player.tempData.enemy!=null)
	    {
		ProtocolBytes prot = new ProtocolBytes();
		prot.AddString("WinGame");
		player.tempData.enemy.Send(prot);
		player.tempData.status = PlayerTempData.Status.None;
		player.tempData.enemy = null;
	    }

        }
    }
    public partial class HandleConnMsg
    {
        //注册
        //协议参数： str用户名 ,str密码
        //返回协议： -1表示失败 0表示成功
        public void MsgRegister(Conn conn, ProtocolBase protoBase)
        {    //获取数值
            int start = 0;
            ProtocolBytes protocol = (ProtocolBytes)protoBase;
            string protoName = protocol.GetString(start, ref start);
            string id = protocol.GetString(start, ref start);
            string pw = protocol.GetString(start, ref start);
            string strFormat = "[收到注册协议 ]" + conn.GetAdress();
            Console.WriteLine(strFormat + " 用户名： " + id + " 密码： " + pw);    //构建返回协议
            protocol = new ProtocolBytes();
            protocol.AddString("Register");    //注册
            if (DataMgr.instance.Register(id, pw))
            {
                protocol.AddInt(0);
                //创建角色
                DataMgr.instance.CreatePlayer(id);
            }
            else
            {
                protocol.AddInt(-1);

            }
            //返回协议给客户端
            conn.Send(protocol);
            Thread.Sleep(10000);
            conn.Close();
        }

        //登录
        //协议参数： str用户名 ,str密码
        //返回协议： -1表示失败 0表示成功
        public void MsgLogin(Conn conn, ProtocolBase protoBase)
        {    //获取数值
            int start = 0;
            ProtocolBytes protocol = (ProtocolBytes)protoBase;
            string protoName = protocol.GetString(start, ref start);
            string id = protocol.GetString(start, ref start);
            string pw = protocol.GetString(start, ref start);
            string strFormat = "[收到登录协议 ]" + conn.GetAdress();
            Console.WriteLine(strFormat + " 用户名： " + id + " 密码： " + pw);    //构建返回协议
            ProtocolBytes protocolRet = new ProtocolBytes();
            protocolRet.AddString("Login");
            //验证
            if (!DataMgr.instance.CheckPassWord(id, pw))
            {
                protocolRet.AddInt(-1);
                conn.Send(protocolRet);
                Thread.Sleep(10000);
                conn.Close();
                return;
            }
            //是否已经登录
            ProtocolBytes protocolLogout = new ProtocolBytes();
            protocolLogout.AddString("Logout");
            if (!Player.KickOff(id, protocolLogout))
            { protocolRet.AddInt(-1); conn.Send(protocolRet); return; }    //获取玩家数据
            PlayerData playerData = DataMgr.instance.GetPlayerData(id); if (playerData == null) { protocolRet.AddInt(-1); conn.Send(protocolRet); return; }
            conn.player = new Player(id, conn); conn.player.data = playerData;    //事件触发
            ServNet.instance.handlePlayerEvent.OnLogin(conn.player);    //返回
            protocolRet.AddInt(0); conn.Send(protocolRet); return;
        }
        //下线
        //协议参数：
        //返回协议： 0-正常下线
        public void MsgLogout(Conn conn, ProtocolBase protoBase)
        {
            ProtocolBytes protocol = new ProtocolBytes();
            protocol.AddString("Logout"); protocol.AddInt(0);
            if (conn.player == null) { conn.Send(protocol); conn.Close(); }
            else { conn.Send(protocol); conn.player.Logout(); }
        }

        public void MsgHeatBeat(Conn conn, ProtocolBase protoBase)
        {
            conn.lastTickTime = Sys.GetTimeStamp();
            Console.WriteLine("[更新心跳时间 ]" + conn.GetAdress());
        }
    }
    public partial class HandlePlayerMsg
    {
        public void MsgStartMatch(Player player, ProtocolBase protocolBase)
        {
		ProtocolBytes proto = (ProtocolBytes)protocolBase;
		int start = 0;
		string protoName = proto.GetString(start, ref start);
		int mode=proto.GetInt(start,ref start);
                ServNet.instance.StartMatch(player,mode);
        }
        public void MsgUpdateLand(Player player, ProtocolBase protocolBase)
        {
            ProtocolBytes proto = (ProtocolBytes)protocolBase;
            player.tempData.enemy.Send(proto);
        }
        public void MsgUpdateMove(Player player, ProtocolBase protocolBase)
        {
            ProtocolBytes proto = (ProtocolBytes)protocolBase;
            player.tempData.enemy.Send(proto);
        }
        public void MsgUpdateAttack(Player player, ProtocolBase protocolBase)
        {
            ProtocolBytes proto = (ProtocolBytes)protocolBase;
            player.tempData.enemy.Send(proto);
        }
        public void MsgSkipMove(Player player, ProtocolBase protocolBase)
        {
            ProtocolBytes proto = (ProtocolBytes)protocolBase;
            player.tempData.enemy.Send(proto);
        }
        public void MsgSkipAttack(Player player, ProtocolBase protocolBase)
        {
            ProtocolBytes proto = (ProtocolBytes)protocolBase;
            player.tempData.enemy.Send(proto);
        }
        public void MsgEndGame(Player player, ProtocolBase protocolBase)
        {
            ProtocolBytes proto = (ProtocolBytes)protocolBase;
            if (player.tempData.enemy != null)
                player.tempData.enemy.Send(proto);
            player.tempData.status = PlayerTempData.Status.None;
            player.tempData.enemy = null;

        }
        public void MsgFindMonster(Player player, ProtocolBase protocolBase)
        {
            ProtocolBytes proto = (ProtocolBytes)protocolBase;
            player.tempData.enemy.Send(proto);
        }
        public void MsgCreateMonster(Player player, ProtocolBase protocolBase)
        {
            ProtocolBytes proto = (ProtocolBytes)protocolBase;
            player.tempData.enemy.Send(proto);
        }
	public void MsgSetBoard(Player player, ProtocolBase protocolBase)
	{
		ProtocolBytes proto = (ProtocolBytes)protocolBase;
		player.tempData.enemy.Send(proto);
	}
	public void MsgStopMatch(Player player, ProtocolBase protocolBase)
	{
		ServNet.instance.StopMatch(player);
	}
	public void MsgGetScore(Player player, ProtocolBase protocolBase)
	{
		ProtocolBytes proto=new ProtocolBytes();
		proto.AddString("GetScore");
		proto.AddInt(player.data.score);
		player.Send(proto);
	}
	public void MsgSetScore(Player player, ProtocolBase protocolBase)
	{
		ProtocolBytes proto = (ProtocolBytes)protocolBase;
		int start = 0;
		string protoName = proto.GetString(start, ref start);
		player.data.score=proto.GetInt(start,ref start);
	}
	public void MsgAddScore(Player player, ProtocolBase protocolBase)
	{			
		ProtocolBytes proto = (ProtocolBytes)protocolBase;
		int start = 0;
		string protoName = proto.GetString(start, ref start);		
		player.data.score+=proto.GetInt(start,ref start);
	}
    }

    public class DataMgr
    {
        MySqlConnection sqlConn;
        public static DataMgr instance;
        public DataMgr()
        {
            instance = this;
            Connect();
        }
        public void Connect()
        {
            string connStr = "Database=game;DataSource=127.0.0.1;";
            connStr += "User Id=root;Password=@maybe13;port=3306;Allow User Variables=True";
            sqlConn = new MySqlConnection(connStr);
            try
            {
                sqlConn.Open();
            }
            catch (Exception e)
            {
                Console.Write("[DataMgr]Connect " + e.Message);
                return;
            }
        }
        public bool IsSafeStr(string str)
        {
            return !Regex.IsMatch(str, @"[-|;|,|\/|\(|\)|\[|\]|\}|\{|%|@|\*|!|\']");
        }

        private bool CanRegister(string id)
        {
            if (!IsSafeStr(id))
                return false;
            string cmdStr = string.Format("select * from user where id='{0}';", id);
            MySqlCommand cmd = new MySqlCommand(cmdStr, sqlConn);
            try
            {
                MySqlDataReader dataReader = cmd.ExecuteReader();
                bool hasRows = dataReader.HasRows;
                dataReader.Close();
                return !hasRows;
            }
            catch (Exception e)
            {
                Console.WriteLine("[DataMgr]CanRegister fail " + e.Message);
                return false;
            }
        }
        public bool Register(string id, string pw)
        {
            if (!IsSafeStr(id) || !IsSafeStr(pw))
            {
                Console.WriteLine("[DataMgr]Register use unsafe char");//change it in unity
                return false;
            }
            if (!CanRegister(id))
            {
                Console.WriteLine("[DataMgr]Register !CanRegister");
                return false;
            }
            string cmdStr = string.Format("insert into user set id='{0}' ,pw='{1}';", id, pw);
            MySqlCommand cmd = new MySqlCommand(cmdStr, sqlConn);
            try
            {
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("[DataMgr]Register " + e.Message);
                return false;
            }
        }
        //创建角色
        public bool CreatePlayer(string id)
        {    //防 sql注入
            if (!IsSafeStr(id)) return false;    //序列化
            IFormatter formatter = new BinaryFormatter();
            MemoryStream stream = new MemoryStream();
            PlayerData playerData = new PlayerData();
            try { formatter.Serialize(stream, playerData); }
            catch (Exception e) { Console.WriteLine("[DataMgr]CreatePlayer 序列化 " + e.Message); return false; }
            byte[] byteArr = stream.ToArray();    //写入数据库
            string cmdStr = string.Format("insert into player set id ='{0}' ,data =@data;", id);
            MySqlCommand cmd = new MySqlCommand(cmdStr, sqlConn); cmd.Parameters.Add("@data", MySqlDbType.Blob);
            cmd.Parameters[0].Value = byteArr; try { cmd.ExecuteNonQuery(); return true; }
            catch (Exception e)
            {
                Console.WriteLine("[DataMgr]CreatePlayer 写入 " + e.Message);
                return false;
            }
        }
        //检测用户名和密码
        public bool CheckPassWord(string id, string pw)
        {    //防 sql注入
            if (!IsSafeStr(id) || !IsSafeStr(pw)) return false;    //查询
            string cmdStr = string.Format("select * from user where id='{0}' and pw='{1}';", id, pw);
            MySqlCommand cmd = new MySqlCommand(cmdStr, sqlConn);
            try { MySqlDataReader dataReader = cmd.ExecuteReader(); bool hasRows = dataReader.HasRows; dataReader.Close(); return hasRows; }
            catch (Exception e) {
                Console.WriteLine("[DataMgr]CheckPassWord " + e.Message);
                if(e.Message=="Connection must be valid and open.")
                {
                    try
                    {
                        Connect();
                    }
                    catch(Exception e2)
                    {
                        Console.WriteLine("Mysql Link Restart"+e2.Message);
                    }
                }
                return false; }
        }
        //获取玩家数据
        public PlayerData GetPlayerData(string id)
        {
            PlayerData playerData = null;    //防 sql注入
            if (!IsSafeStr(id)) return playerData;    //查询
            string cmdStr = string.Format("select * from player where id ='{0}';", id);
            MySqlCommand cmd = new MySqlCommand(cmdStr, sqlConn);
            byte[] buffer = new byte[1];
            try
            {
                MySqlDataReader dataReader = cmd.ExecuteReader();
                if (!dataReader.HasRows) { dataReader.Close(); return playerData; }
                dataReader.Read();
                long len = dataReader.GetBytes(1, 0, null, 0, 0);//1是 data          
                buffer = new byte[len];
                dataReader.GetBytes(1, 0, buffer, 0, (int)len);
                dataReader.Close();
            }
            catch (Exception e) { Console.WriteLine("[DataMgr]GetPlayerData 查询 " + e.Message); return playerData; }    //反序列化
            MemoryStream stream = new MemoryStream(buffer);
            try { BinaryFormatter formatter = new BinaryFormatter(); playerData = (PlayerData)formatter.Deserialize(stream); return playerData; }
            catch (SerializationException e) { Console.WriteLine("[DataMgr]GetPlayerData 反序列化 " + e.Message); return playerData; }
        }
        //保存角色
        public bool SavePlayer(Player player)
        {
            string id = player.id;
            PlayerData playerData = player.data;    //序列化
            IFormatter formatter = new BinaryFormatter();
            MemoryStream stream = new MemoryStream();
            try { formatter.Serialize(stream, playerData); }
            catch (Exception e) { Console.WriteLine("[DataMgr]SavePlayer 序列化 " + e.Message); return false; }
            byte[] byteArr = stream.ToArray();    //写入数据库
            string formatStr = "update player set data =@data where id = '{0}';";
            string cmdStr = string.Format(formatStr, player.id);
            MySqlCommand cmd = new MySqlCommand(cmdStr, sqlConn);
            cmd.Parameters.Add("@data", MySqlDbType.Blob);
            cmd.Parameters[0].Value = byteArr;
            try { cmd.ExecuteNonQuery(); return true; }
            catch (Exception e) { Console.WriteLine("[DataMgr]SavePlayer 写入 " + e.Message); return false; }
        }
    }

    public class ServNet
    {
        public Conn[] conns;
        public int maxConn = 50;
        public static ServNet instance;
        public Socket listenfd;
        //主定时器
        System.Timers.Timer timer = new System.Timers.Timer(1000); //心跳时间
        public long heartBeatTime = 180;
        public ProtocolBase proto;
        public HandleConnMsg handleConnMsg = new HandleConnMsg();
        public HandlePlayerMsg handlePlayerMsg = new HandlePlayerMsg();
        public HandlePlayerEvent handlePlayerEvent = new HandlePlayerEvent();
        public List<WaitingPlayer>[] MatchingPlayers;
	public int ModeNum=10;
        public struct WaitingPlayer
        {
            public Player player;
            public int waitTime;
	    public int mode;
        };
        public ServNet()
        {
            instance = this;
        }
        public int NewIndex()
        {
            if (conns == null)
                return -1;
            for (int i = 0; i < conns.Length; i++)
            {
                if (conns[i] == null)
                {
                    conns[i] = new Conn();
                    return i;
                }
                else if (conns[i].isUse == false)
                    return i;

            }
            return -1;
        }
        public void HandleMainTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            //处理匹配
            FindMatch();
            //处理心跳
            HeartBeat();
            timer.Start();
        } //心跳
        public void HeartBeat()
        {
            //Console.WriteLine("[主定时器执行 ]");
            long timeNow = Sys.GetTimeStamp();
            for (int i = 0; i < conns.Length; i++)
            {
                Conn conn = conns[i];
                if (conn == null) continue;
                if (!conn.isUse) continue;
                if (conn.lastTickTime < timeNow - heartBeatTime)
                { Console.WriteLine("[心跳引起断开连接 ]" + conn.GetAdress()); lock (conn) conn.Close(); }
            }
        }
        public void Start(string host, int port)
        {
            //定时器
            timer.Elapsed += new System.Timers.ElapsedEventHandler(HandleMainTimer);
            timer.AutoReset = false; timer.Enabled = true;
            conns = new Conn[maxConn];
            for (int i = 0; i < maxConn; i++)
                conns[i] = new Conn();
            listenfd = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //Bind
            IPAddress ipAdr = IPAddress.Parse(host);
            IPEndPoint ipEp = new IPEndPoint(ipAdr, port);
            listenfd.Bind(ipEp);
            listenfd.Listen(maxConn);
            listenfd.BeginAccept(AcceptCb, null);
	    MatchingPlayers=new List<WaitingPlayer>[ModeNum];
	    for(int i=0;i<ModeNum;i++)
	    {
            	MatchingPlayers[i]=new List<WaitingPlayer>();
	    }
            Console.WriteLine("[Server]start success");
        }
        private void AcceptCb(IAsyncResult ar)
        {
            try
            {
                Socket socket = listenfd.EndAccept(ar);
                int index = NewIndex();
                if (index < 0)
                {
                    socket.Close();
                    Console.Write("[Warning]Link Is Full");
                }
                else
                {
                    Conn conn = conns[index];
                    conn.Init(socket);
                    string adr = conn.GetAdress();
                    Console.WriteLine("Customer Link[" + adr + "] conn pool id:" + index);
                    conn.socket.BeginReceive(conn.readBuff, conn.buffCount, conn.BuffRemain(), SocketFlags.None, ReceiveCb, conn);
                }
                listenfd.BeginAccept(AcceptCb, null);
            }
            catch (Exception e)
            {
                Console.WriteLine("AcceptCb Fail:" + e.Message);
            }
        }
        public void Close()
        {
            for (int i = 0; i < conns.Length; i++)
            {
                Conn conn = conns[i];
                if (conn == null) continue;
                if (!conn.isUse) continue;
                lock (conn)
                    conn.Close();
            }
        }
        private void ReceiveCb(IAsyncResult ar)
        {
            Conn conn = (Conn)ar.AsyncState;
            lock (conn)
            {
                try
                {
                    int count = conn.socket.EndReceive(ar);            //关闭信号
                    if (count <= 0)
                    {
                        Console.WriteLine("Receive[" + conn.GetAdress() + "]Link Close");
                        conn.Close();
                        return;
                    }
                    conn.buffCount += count;
                    ProcessData(conn);            //继续接收                
                    conn.socket.BeginReceive(conn.readBuff, conn.buffCount, conn.BuffRemain(), SocketFlags.None, ReceiveCb, conn);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Receive[" + "]Link Close");
                    conn.Close();
                }
            }
        }
        private void ProcessData(Conn conn)
        {    //小于长度字节
            if (conn.buffCount < sizeof(Int32)) { return; }    //消息长度
            Array.Copy(conn.readBuff, conn.lenBytes, sizeof(Int32));
            conn.msgLength = BitConverter.ToInt32(conn.lenBytes, 0);
            if (conn.buffCount < conn.msgLength + sizeof(Int32))
            { return; }    //处理消息
            ProtocolBase protocol = proto.Decode(conn.readBuff, sizeof(Int32), conn.msgLength);
            HandleMsg(conn, protocol);
            //清除已处理的消息
            int count = conn.buffCount - conn.msgLength - sizeof(Int32);
            Array.Copy(conn.readBuff, sizeof(Int32) + conn.msgLength, conn.readBuff, 0, count);
            conn.buffCount = count;
            if (conn.buffCount > 0)
            {
                ProcessData(conn);
            }
        }
        private void HandleMsg(Conn conn, ProtocolBase protoBase)
        {
            string name = protoBase.GetName();
            string methodName = "Msg" + name;    //连接协议分发
            if (conn.player == null || name == "HeatBeat" || name == "Logout")
            {
                MethodInfo mm = handleConnMsg.GetType().GetMethod(methodName);
                if (mm == null)
                {
                    string str = "[警告 ]HandleMsg没有处理连接方法 ";
                    Console.WriteLine(str + methodName);
                    return;
                }
                Object[] obj = new object[] { conn, protoBase };
                Console.WriteLine("[处理连接消息 ]" + conn.GetAdress() + " :" + name);
                mm.Invoke(handleConnMsg, obj);
            }    //角色协议分发
            else
            {
                MethodInfo mm = handlePlayerMsg.GetType().GetMethod(methodName);
                if (mm == null)
                {
                    string str = "[警告 ]HandleMsg没有处理玩家方法 ";
                    Console.WriteLine(str + methodName);
                    return;
                }
                Object[] obj = new object[] { conn.player, protoBase };
                Console.WriteLine("[处理玩家消息 ]" + conn.player.id + " :" + name);
                mm.Invoke(handlePlayerMsg, obj);
            }
        }


        public void Send(Conn conn, ProtocolBase protocol)
        {
            byte[] bytes = protocol.Encode();
            byte[] length = BitConverter.GetBytes(bytes.Length);
            byte[] sendbuff = length.Concat(bytes).ToArray();
            try { conn.socket.BeginSend(sendbuff, 0, sendbuff.Length, SocketFlags.None, null, null); }
            catch (Exception e) { Console.WriteLine("[发送消息 ]" + conn.GetAdress() + " : " + e.Message); }
        }
        public void Broadcast(ProtocolBase protocol)
        {
            for (int i = 0; i < conns.Length; i++)
            {
                if (!conns[i].isUse)
                    continue;
                if (conns[i].player == null)
                    continue;
                Send(conns[i], protocol);
            }
        }


        public void StartMatch(Player PlayerToMatch,int mode)
        {
            WaitingPlayer waitingPlayer;
            waitingPlayer.player = PlayerToMatch;
            waitingPlayer.waitTime = 0;
	    waitingPlayer.mode=mode;
            MatchingPlayers[mode].Add(waitingPlayer);
        }
	public void StopMatch(Player player)
	{
	    for(int i=0;i<ModeNum;i++)
	    {
		for(int j=0;j<MatchingPlayers[i].Count;j++)
		{
			if(MatchingPlayers[i][j].player==player)
			{
		    		MatchingPlayers[i].RemoveAt(j);
		    		return;
			}
		}
	    }
	}
        public void FindMatch()
        {
            for(int j=0;j<ModeNum;j++)
	    {
            while (MatchingPlayers[j].Count >= 2)
            {
                //开始战斗协议，只包含每个玩家队伍(0/1)与是否用AI替代（0（No），1）,Mode(0,1,...)
                ProtocolBytes protocol = new ProtocolBytes();
                protocol.AddString("StartFight");
                MatchingPlayers[j][0].player.tempData.status = PlayerTempData.Status.Fight;
                MatchingPlayers[j][0].player.tempData.enemy = MatchingPlayers[j][1].player;
                protocol.AddInt(0);
                protocol.AddInt(0);
		protocol.AddInt(MatchingPlayers[j][0].mode);
                MatchingPlayers[j][0].player.Send(protocol);
                protocol = new ProtocolBytes();
                protocol.AddString("StartFight");
                MatchingPlayers[j][1].player.tempData.status = PlayerTempData.Status.Fight;
                MatchingPlayers[j][1].player.tempData.enemy = MatchingPlayers[j][0].player;
                protocol.AddInt(1);
                protocol.AddInt(0);
		protocol.AddInt(MatchingPlayers[j][1].mode);
                MatchingPlayers[j][1].player.Send(protocol);
                MatchingPlayers[j].RemoveAt(0);
                MatchingPlayers[j].RemoveAt(0);
            }
            for (int i = 0; i < MatchingPlayers[j].Count; i++)
            {
                
                if (MatchingPlayers[j][i].waitTime > 15)
                {
                    //等待过长，AI替代
                    ProtocolBytes protocol = new ProtocolBytes();
                    protocol.AddString("StartFight");
                    System.Random a = new Random(System.DateTime.Now.Millisecond); // use System.DateTime.Now.Millisecond as seed
                    int RandKey = a.Next(0, 2);
                    Console.WriteLine(RandKey);
                    protocol.AddInt(RandKey);
                    protocol.AddInt(1);
		    protocol.AddInt(MatchingPlayers[j][i].mode);
                    MatchingPlayers[j][i].player.Send(protocol);
                    Console.WriteLine("Start a fight using AI");
                    MatchingPlayers[j].RemoveAt(i);
                }
                if (i < MatchingPlayers[j].Count)
                {
                    WaitingPlayer waitingPlayer = MatchingPlayers[j][i];
                    waitingPlayer.waitTime = MatchingPlayers[j][i].waitTime + 1;
                    MatchingPlayers[j][i] = waitingPlayer;
                }
            }
        }
	}
    }
    public class Sys
    {
        public static long GetTimeStamp()
        {
            TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return Convert.ToInt64(ts.TotalSeconds);
        }
    }
    public class ProtocolBase
    {
        public virtual ProtocolBase Decode(byte[] readbuff, int start, int length)
        {
            return new ProtocolBase();
        }
        public virtual byte[] Encode()
        {
            return new byte[] { };
        }
        public virtual string GetName()
        {
            return "";
        }
        public virtual string GetDesc()
        {
            return "";

        }
    }
    public class ProtocolBytes : ProtocolBase
    {
        public byte[] bytes;
        public override ProtocolBase Decode(byte[] readbuff, int start, int length)
        {
            ProtocolBytes protocol = new ProtocolBytes();
            protocol.bytes = new byte[length];
            Array.Copy(readbuff, start, protocol.bytes, 0, length);
            return protocol;
        }
        public override byte[] Encode()
        {
            return bytes;
        }
        public override string GetName()
        {
            return GetString(0);
        }
        public override string GetDesc()
        {
            string str = "";
            if (bytes == null)
                return str;
            for (int i = 0; i < bytes.Length; i++)
            {
                int b = (int)bytes[i];
                str += b.ToString() + " ";
            }
            return str;
        }
        public void AddString(string str)
        {
            Int32 len = str.Length;
            byte[] lenBytes = BitConverter.GetBytes(len);
            byte[] strBytes = System.Text.Encoding.UTF8.GetBytes(str);
            if (bytes == null)
                bytes = lenBytes.Concat(strBytes).ToArray();
            else
                bytes = bytes.Concat(lenBytes).Concat(strBytes).ToArray();
        }
        public string GetString(int start, ref int end)
        {
            if (bytes == null)
                return "";
            if (bytes.Length < start + sizeof(Int32))
                return "";
            Int32 strLen = BitConverter.ToInt32(bytes, start);
            if (bytes.Length < start + sizeof(Int32) + strLen)
                return "";
            string str = System.Text.Encoding.UTF8.GetString(bytes, start + sizeof(Int32), strLen);
            end = start + sizeof(Int32) + strLen;
            return str;
        }
        public string GetString(int start)
        {
            int end = 0;
            return GetString(start, ref end);
        }
        public void AddInt(int num)
        {
            byte[] numBytes = BitConverter.GetBytes(num);
            if (bytes == null)
                bytes = numBytes;
            else
                bytes = bytes.Concat(numBytes).ToArray();
        }
        public int GetInt(int start, ref int end)
        {
            if (bytes == null)
                return 0;
            if (bytes.Length < start + sizeof(Int32))
                return 0;
            end = start + sizeof(Int32);
            return BitConverter.ToInt32(bytes, start);
        }
        public int GetInt(int start)
        {
            int end = 0;
            return GetInt(start, ref end);
        }
        public void AddFloat(float num)
        {
            byte[] numBytes = BitConverter.GetBytes(num);
            if (bytes == null)
                bytes = numBytes;
            else
                bytes = bytes.Concat(numBytes).ToArray();
        }
        public float GetFloat(int start, ref int end)
        {
            if (bytes == null)
                return 0;
            if (bytes.Length < start + sizeof(Int32))
                return 0;
            end = start + sizeof(Int32);
            return BitConverter.ToSingle(bytes, start);
        }
        public float GetFloat(int start)
        {
            int end = 0;
            return GetFloat(start, ref end);
        }

    }

    class Program
    {
        static void Main(string[] args)
        {
            DataMgr dataMgr = new DataMgr();
            ServNet servNet = new ServNet();
            servNet.Start("0.0.0.0", 1234);
            servNet.proto = new ProtocolBytes();
            MatchMgr roomMgr = new MatchMgr();
            while (true)
            {
                Thread.Sleep(1000);
            }
            /*
            DataMgr dataMgr = new DataMgr();
            bool ret=dataMgr.Register("test2","123");
            if (ret)
                Console.WriteLine("Register Success");
            else
                Console.WriteLine("Register Fail");
            ret = dataMgr.CreatePlayer("test2");
            if (ret)
                Console.WriteLine("Create Player Success");
            else
                Console.WriteLine("Create Player Fail");
            PlayerData pd = dataMgr.GetPlayerData("test");
            if (pd!=null)
                Console.WriteLine("Getplayer Success"+pd.score);
            else
                Console.WriteLine("Getplayer Fail");
            pd.score += 10;
            Player p = new Player();
            p.id = "test";
            p.data = pd;
            dataMgr.SavePlayer(p);
            pd = dataMgr.GetPlayerData("test");
            if (pd != null)
                Console.WriteLine("Getplayer Success" + pd.score);
            else
                Console.WriteLine("Getplayer Fail");
            Console.Read();*/
        }
    }
    public class MatchMgr
    {


    }

}
