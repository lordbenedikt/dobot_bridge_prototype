using System;
using System.Text;
using DobotBridge.CPlusDll;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;
using System.Threading;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace DobotBridge
{
    /*
     * Controller subscribes to following topic and awaits commands:
     *    Dobot/ProgramNumber
     * Controller publishes messages to following topics:
     *    Dobot/Start
     *    Dobot/Error
     *    Dobot/CommunicateError
     *    Dobot/Finish
     * 
     * --- console commands ---
     * run filename => execute commands previously saved in a .playback file. If missing '.playback' is automatically appended.
     * connect => attempt connecting to dobot
     * disconnect => disconnect from dobot
     * setSpeed {velocity},{acceleration} => set dobot movement speed to given values
     * home => return to home position
     * start => start execution of queued commands
     * stop => stop execution of queued commands
     * stats => show current pose and infrared sensor state
     * clear => clear command queue and alarm states
     * getPose => print current dobot pose
     * exit => exit application
     */
    class DobotController
    {
        static int lastCommunicateIndex = 0;
        static int lastError = 0;
        static int maxVelocity = 700;
        static int maxAcceleration = 700;

        static XMLReader xmlReader = new XMLReader();
        static Boolean isConnected = false;
        static Pose pose = new Pose();
        static System.Timers.Timer posTimer = new System.Timers.Timer();
        static HOMECmd homeCmd = new HOMECmd();
        static PTPJointParams ptpJointParams = new PTPJointParams();
        static ulong queuedCmdIndex = 0;
        static ulong executedCmdIndex = 0;
        static UInt32 alarmState = 0;
        static byte infraredSensor = 9;

        static bool isRunning = false;
        static bool isStopped = false;
        static Thread threadDobot = new Thread(new ThreadStart(CheckState));
        static Thread checkConnection = new Thread(new ThreadStart(CheckConnectionState));

        static MqttClient mqttClient;
        const string brokerHostName = "broker.hivemq.com";
        const string clientName = "dobotClient";

        static void Main(string[] args)
        {
            Initialize();
            checkConnection.Start();
            DobotDll.SetCmdTimeout(20);
            WaitForUserInput();
            DobotDll.DisconnectDobot();
        }

        static void Initialize()
        {
            // Initialize MqttClient
            mqttClient = new MqttClient(brokerHostName);
            mqttClient.Connect(Guid.NewGuid().ToString());
            mqttClient.MqttMsgPublishReceived += OnMqttPublishReceived;
            mqttClient.Subscribe(new string[] { TopicHelper.ProgramNumber }, new byte[] { 2 });

            // connect and initialize
            Console.WriteLine("Connecting to Dobot...");
            StringBuilder fwType = new StringBuilder(60);
            StringBuilder version = new StringBuilder(60);

            int ret = DobotDll.ConnectDobot("", 115200, fwType, version);
            if (ret == (int)DobotConnect.DobotConnect_NoError)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Connected successfully - type={0}, version={1}", fwType, version);
                Console.ForegroundColor = ConsoleColor.White;
                isConnected = true;
            }
            else if (ret == (int)DobotConnect.DobotConnect_NotFound)
            {
                ShowErrorMsg("DobotConnect_NotFound");
                return;
            }
            else if (ret == (int)DobotConnect.DobotConnect_Occupied)
            {
                ShowErrorMsg("DobotConnect_Occupied");
                return;
            }

            // set home parameters
            HOMEParams home = new HOMEParams
            {
                x = 250,
                y = 0,
                z = 85,
                r = 0
            };

            DobotDll.SetHOMEParams(ref home, true, ref queuedCmdIndex);
            //DobotDll.SetHOMECmd(ref homeCmd, false, ref queuedCmdIndex);

            // enable infrared sensor
            DobotDll.SetInfraredSensor(1, InfraredPort.IF_PORT_GP2, 1);

            // enable color sensor
            DobotDll.SetColorSensor(true, ColorPort.IF_PORT_GP4, 1);
            isRunning = true;
        }

        public static void CheckConnectionState()
        {
            while(true)
            {
                if(!isConnected)
                {
                    Initialize();
                }
                Thread.Sleep(500);
            }
        }

        public static void CheckState()
        {
            while (isRunning)
            {
                byte[] alarmStateArray = new byte[32];
                UInt32 alarm = 0;
                DobotDll.GetQueuedCmdCurrentIndex(ref executedCmdIndex);
                DobotDll.GetAlarmsState(alarmStateArray, ref alarm, 32);

                // On Error
                for (int i = 0; i < alarmStateArray.Length; i++)
                {
                    if (alarmStateArray[i] != 0)
                    {
                        mqttClient.Publish(TopicHelper.Error, new byte[] { });
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("OnError(): {0} | {1}", i, alarmStateArray[i]);
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write(">> ");
                        alarmState = 0;
                        return;
                    }
                }

                // On Communicate Error
                if (lastCommunicateIndex != 0)
                {
                    if(lastCommunicateIndex == (int)DobotCommunicate.DobotCommunicate_Timeout)
                    {
                        isConnected = false;
                    }
                    mqttClient.Publish(TopicHelper.CommunicateError, new byte[] { });
                    Console.ForegroundColor = ConsoleColor.Red;
                    PrintError(lastCommunicateIndex);
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(">> ");
                    lastError = lastCommunicateIndex;
                    lastCommunicateIndex = 0;
                }

                // Check for alarms
                if (alarm != 0)
                {
                    // On Start Execution
                    if (alarm == 16 && alarmState != alarm)
                    {
                        alarmState = alarm;
                        mqttClient.Publish(TopicHelper.Start, new byte[] { });
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine("OnStart()");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write(">> ");
                    }
                    else if (alarm != 16)
                    {
                        Console.WriteLine("Alarm: " + alarm);
                    }
                }

                // On Finish
                if (queuedCmdIndex == executedCmdIndex)
                {
                    mqttClient.Publish(TopicHelper.Finish, new byte[] { });
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine("OnFinish()");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(">> ");
                    alarmState = 0;
                    return;
                }

                Thread.Sleep(10);
            }
        }

        static void OnMqttPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            string message = Encoding.Default.GetString(e.Message);

            if (message == "0")
            {
                ClearQueue();
                lastCommunicateIndex = DobotDll.SetHOMECmd(ref homeCmd, false, ref queuedCmdIndex);
                StartCheckState();
            }
            else
            {
                string filename = String.Format("program_{0}", message);
                Console.WriteLine("run {0}", filename);
                Console.Write(">> ");
                EnqueuePlayback(filename);
            }
        }

        static void StartCheckState()
        {
            if (!threadDobot.IsAlive)
            {
                threadDobot = new Thread(new ThreadStart(CheckState));
                threadDobot.Start();
            }
        }

        static void WaitForUserInput()
        {
            var run = false;
            var setFilename = false;
            var setSpeed = false;

            while (true)
            {
                Console.Write(">> ");
                string input = Console.ReadLine().Trim();
                string filename = "";
                string[] arguments = input.Split(" ");

                foreach (string argument in arguments)
                {
                    if (argument == "connect")
                    {
                        Initialize();
                    }
                    else if (argument == "disconnect")
                    {
                        DobotDll.DisconnectDobot();
                        isConnected = false;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Disconnected from Dobot");
                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    else if (!isConnected)
                    {
                        ShowErrorMsg("please connect to Dobot!");
                        break;
                    }

                    else if (setFilename)
                    {
                        filename = argument;
                        setFilename = false;
                    }

                    else if (setSpeed)
                    {
                        string[] speed = argument.Split(",");
                        try
                        {
                            float velInput = float.Parse(speed[0]);
                            float accInput = float.Parse(speed[1]);
                            float vel = Math.Clamp(velInput, 0, maxVelocity);
                            float acc = Math.Clamp(accInput, 0, maxAcceleration);
                            if (velInput != vel || accInput != acc)
                            {
                                Console.WriteLine("Values out of bound! Clamped to velocity:{0}/acceleration:{1}", vel, acc);
                            }
                            setDobotSpeed(vel, acc);
                        }
                        catch
                        {
                            Console.WriteLine("Parsing Error!");
                        }

                        setSpeed = false;
                    }

                    else if (argument == "exit")
                    {
                        return;
                    }

                    else if (argument == "run")
                    {
                        run = true;
                        setFilename = true;
                    }

                    else if (argument == "infrared")
                    {
                        DobotDll.GetInfraredSensor(InfraredPort.IF_PORT_GP2, ref infraredSensor);
                        Console.WriteLine("infrared: {0}", infraredSensor);
                    }

                    else if (argument == "motor")
                    {
                        SetMotorSpeedAndDistance(0, 1, 10000, 10000);
                        StartCheckState();
                    }

                    else if (argument == "motorSpeed")
                    {
                        SetMotorSpeed(0, 1, 10000);
                    }

                    else if (argument == "color")
                    {
                        byte r = 0;
                        byte g = 0;
                        byte b = 0;
                        DobotDll.GetColorSensor(ref r, ref g, ref b);
                        Console.WriteLine("r:{0},g:{1},b:{2}", r, g, b);
                    }

                    else if (argument == "home")
                    {
                        lastCommunicateIndex = DobotDll.SetHOMECmd(ref homeCmd, false, ref queuedCmdIndex);
                        StartCheckState();
                    }

                    else if (argument == "start")
                    {
                        lastCommunicateIndex = DobotDll.SetQueuedCmdStartExec();
                        isStopped = false;
                    }

                    else if (argument == "stop")
                    {
                        lastCommunicateIndex = DobotDll.SetQueuedCmdStopExec();
                        isStopped = true;
                    }

                    else if (argument == "stats")
                    {
                        ShowStats();
                    }

                    else if (argument == "clear")
                    {
                        ClearQueue();
                    }

                    else if (argument == "getPose")
                    {
                        lastCommunicateIndex = DobotDll.GetPose(ref pose);
                        Console.WriteLine("x={0} y={1} z={2} rHead={3} jointAngles=[{4}:{5}:{6}:{7}]",
                            pose.x, pose.y, pose.z, pose.rHead, pose.jointAngle[0], pose.jointAngle[1], pose.jointAngle[2], pose.jointAngle[3]);
                    }
                    else if (argument == "setSpeed")
                    {
                        setSpeed = true;
                    }
                }

                // add poses from .playback-file to command queue
                if (run)
                {
                    EnqueuePlayback(filename);  // open .playback file and execute
                    run = false;                // reset run variable
                }
                setSpeed = false;               // reset setSpeed variable

            }
        }

        static void setDobotSpeed(float vel, float acc)
        {
            float[] velocity = { vel, vel, vel, vel };
            float[] acceleration = { acc, acc, acc, acc };
            ptpJointParams.velocity = velocity;
            ptpJointParams.acceleration = acceleration;
            DobotDll.SetPTPJointParams(ref ptpJointParams, true, ref queuedCmdIndex);
        }

        static void EnqueuePlayback(string filename)
        {
            // append .playback to filename
            string[] segments = filename.Split(".");
            if (segments.Last<string>() != "playback")
            {
                filename = filename + ".playback";
            }
            // if file exists add poses to queue
            DobotPose[] poses = xmlReader.getPoses(filename);
            if (poses != null)
            {
                try
                {
                    Enqueue(poses);
                    StartCheckState();
                }
                catch { }
            }
        }

        static void SetMotorSpeed(byte index, byte isEnabled, UInt32 speed)
        {
            EMotor eMotor = new EMotor
            {
                index = index,
                isEnabled = isEnabled,
                speed = speed
            };
            lastCommunicateIndex = DobotDll.SetEMotor(ref eMotor, true, ref queuedCmdIndex);
        }

        static void SetMotorSpeedAndDistance(byte index, byte isEnabled, UInt32 speed, UInt32 distance)
        {
            EMotorS eMotorS = new EMotorS
            {
                index = index,
                isEnabled = isEnabled,
                speed = speed,
                distance = distance
            };
            lastCommunicateIndex = DobotDll.SetEMotorS(ref eMotorS, true, ref queuedCmdIndex);
        }

        static void ClearQueue()
        {
            lastCommunicateIndex = DobotDll.ClearAllAlarmsState();
            lastCommunicateIndex = DobotDll.SetQueuedCmdClear();
            // disable suctionCap/gripper
            DobotDll.SetEndEffectorSuctionCup(false, true, true, ref queuedCmdIndex);
            DobotDll.SetQueuedCmdStartExec();
            lastCommunicateIndex = DobotDll.SetQueuedCmdClear();
        }

        static void Enqueue(DobotPose[] poses)
        {
            foreach (DobotPose pose in poses)
            {
                Enqueue(pose);
            }
        }

        static void Enqueue(DobotPose pose)
        {
            // arc motion style
            if (pose.style == 3)
            {
                // fehlerhaft!
                ARCCmd arcCmd;

                arcCmd.toPoint_x = pose.x;
                arcCmd.toPoint_y = pose.y;
                arcCmd.toPoint_z = pose.z;
                arcCmd.toPoint_r = pose.r;
                arcCmd.cirPoint_x = pose.xx;
                arcCmd.cirPoint_y = pose.yy;
                arcCmd.cirPoint_z = pose.zz;
                arcCmd.cirPoint_r = pose.rr;

                lastCommunicateIndex = DobotDll.SetARCCmd(ref arcCmd, true, ref queuedCmdIndex);
            }
            // all other motion styles
            else
            {
                PTPCmd pdbCmd;

                pdbCmd.ptpMode = pose.style;
                pdbCmd.x = pose.x;
                pdbCmd.y = pose.y;
                pdbCmd.z = pose.z;
                pdbCmd.rHead = pose.r;

                lastCommunicateIndex = DobotDll.SetPTPCmd(ref pdbCmd, true, ref queuedCmdIndex);
            }

            // set suction cup/gripper
            if (pose.endEffector == EndType.EndTypeSuctionCap)
            {
                lastCommunicateIndex = DobotDll.SetEndEffectorSuctionCup(pose.gripper == 1, true, true, ref queuedCmdIndex);
            }
            else if (pose.endEffector == EndType.EndTypeGripper)
            {
                lastCommunicateIndex = DobotDll.SetEndEffectorGripper(pose.gripper != 0, pose.gripper == 2, true, ref queuedCmdIndex);
            }

            // set pause time
            WAITCmd waitCmd = new WAITCmd();
            waitCmd.timeout = pose.pauseTime;
            lastCommunicateIndex = DobotDll.SetWAITCmd(ref waitCmd, true, ref queuedCmdIndex);
        }


        static void ShowErrorMsg(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
            Console.ForegroundColor = ConsoleColor.White;
        }

        static void PrintError(int error)
        {
            switch ((DobotCommunicate)error)
            {
                case DobotCommunicate.DobotCommunicate_NoError:
                    { Console.WriteLine("DobotCommunicate_NoError"); }
                    break;
                case DobotCommunicate.DobotCommunicate_BufferFull:
                    { Console.WriteLine("DobotCommunicate_BufferFull"); }
                    break;
                case DobotCommunicate.DobotCommunicate_Timeout:
                    { Console.WriteLine("DobotCommunicate_Timeout"); }
                    break;
                case DobotCommunicate.DobotCommunicate_InvalidParams:
                    { Console.WriteLine("DobotCommunicate_InvalidParams"); }
                    break;
            }
        }

        public static void ShowStats()
        {
            lastCommunicateIndex = DobotDll.GetPose(ref pose);
            Console.WriteLine("pose:\t\t\tx={0} y={1} z={2} rHead={3}",
                pose.x, pose.y, pose.z, pose.rHead);
            Console.WriteLine("\t\t\tjointAngles=[{0}:{1}:{2}:{3}]", pose.jointAngle[0], pose.jointAngle[1], pose.jointAngle[2], pose.jointAngle[3]);
            Console.WriteLine("infrared sensor:\t{0}", infraredSensor);
        }
    }

    class DobotPose
    {
        public byte style;
        public string name;
        public float x;
        public float y;
        public float z;
        public float r;
        public float xx;
        public float yy;
        public float zz;
        public float rr;
        public uint pauseTime;
        public int gripper;
        public EndType endEffector = 0;

        NumberFormatInfo numFormat = CultureInfo.InvariantCulture.NumberFormat;

        public DobotPose(float x, float y, float z, float r)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.r = r;
        }

        public DobotPose(XElement row)
        {
            style = byte.Parse(row.Element("item_0").Value);
            name = row.Element("item_1").Value;
            x = float.Parse(row.Element("item_2").Value, numFormat);
            y = float.Parse(row.Element("item_3").Value, numFormat);
            z = float.Parse(row.Element("item_4").Value, numFormat);
            r = float.Parse(row.Element("item_5").Value, numFormat);
            // if arc motion style
            if (style == 3)
            {
                xx = float.Parse(row.Element("item_6").Value, numFormat);
                yy = float.Parse(row.Element("item_7").Value, numFormat);
                zz = float.Parse(row.Element("item_8").Value, numFormat);
                rr = float.Parse(row.Element("item_9").Value, numFormat);
            }
            pauseTime = (uint)(float.Parse(row.Element("item_10").Value, numFormat) * 1000);
            if (row.Element("item_11") != null)
            {
                endEffector = EndType.EndTypeGripper;
                gripper = int.Parse(row.Element("item_11").Value, numFormat);
            }
            else if (row.Element("item_12") != null)
            {
                endEffector = EndType.EndTypeSuctionCap;
                gripper = int.Parse(row.Element("item_12").Value, numFormat);
            }
        }
    }

    class XMLReader
    {
        public DobotPose[] getPoses(string file)
        {
            XElement playback;
            try
            {
                playback = XElement.Load(file);
            }
            catch (Exception)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("file '{0}' not found!", file);
                Console.ForegroundColor = ConsoleColor.White;
                return null;
            }
            DobotPose[] poses;
            try
            {
                var elements = playback.Elements().ToList<XElement>();
                poses = new DobotPose[elements.Count() - 2];
                for (int i = 2; i < elements.Count(); i++)
                {
                    poses[i - 2] = new DobotPose(elements[i]);
                }
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("wrong format!", file);
                Console.ForegroundColor = ConsoleColor.White;
                return null;
            }
            return poses;
        }
    }

    class TopicHelper
    {
        public readonly static string Start = "Dobot/Start";
        public readonly static string ProgramNumber = "Dobot/ProgramNumber";
        public readonly static string Error = "Dobot/Error";
        public readonly static string CommunicateError = "Dobot/CommunicateError";
        public readonly static string Finish = "Dobot/Finish";
    }
}
