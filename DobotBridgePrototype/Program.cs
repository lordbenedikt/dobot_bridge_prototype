using System;
using System.Text;
using DobotBridgePrototype.CPlusDll;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;

namespace DobotBridgePrototype
{
    class Program
    {
        static XMLReader xmlReader = new XMLReader();
        static byte isJoint = (byte)0;
        static Boolean isConnected = false;
        static JogCmd currentCmd;
        static Pose pose = new Pose();
        static System.Timers.Timer posTimer = new System.Timers.Timer();
        static HOMECmd homeCmd = new HOMECmd();
        static ulong queuedCmdIndex = 0;

        static void Main(string[] args)
        {
            Initialize();
            WaitForUserInput();
            DobotDll.DisconnectDobot();
        }

        static void Initialize()
        {
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
        }

        static void WaitForUserInput()
        {
            var run = false;
            var setFilename = false;
            var setGripper = false;

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
                        if (!isConnected)
                        {
                            Initialize();
                        }
                        else
                        {
                            Console.WriteLine("already connected to Dobot");
                        }
                    }

                    else if (!isConnected)
                    {
                        ShowErrorMsg("please connect to Dobot!");
                        break;
                    }

                    else if (setFilename && filename == "")
                    {
                        filename = argument;
                        setFilename = false;
                    }

                    else if (argument == "exit")
                    {
                        return;
                    }

                    else if (setGripper)
                    {
                        UInt64 cmdIndex = 0;
                        if (argument == "on")
                        {
                            while (true)
                            {
                                int ret = DobotDll.SetEndEffectorSuctionCup(true, true, true, ref cmdIndex);
                                if (ret == 0)
                                    break;
                            }
                        }
                        if (argument == "off")
                        {
                            while (true)
                            {
                                int ret = DobotDll.SetEndEffectorSuctionCup(false, true, true, ref cmdIndex);
                                if (ret == 0)
                                    break;
                            }
                        }
                    }

                    else if (argument == "gripper")
                    {
                        setGripper = true;
                    }

                    else if (argument == "run")
                    {
                        run = true;
                        setFilename = true;
                    }

                    else if (argument == "home")
                    {
                        DobotDll.SetHOMECmd(ref homeCmd, false, ref queuedCmdIndex);
                    }

                    else if (argument == "start")
                    {
                        DobotDll.SetQueuedCmdStartExec();
                    }

                    else if (argument == "stop")
                    {
                        DobotDll.SetQueuedCmdStopExec();
                    }

                    else if (argument == "clear")
                    {
                        DobotDll.SetQueuedCmdClear();
                    }

                    else if (argument == "getPose")
                    {
                        DobotDll.GetPose(ref pose);
                        Console.WriteLine("x={0} y={1} z={2} rHead={3} jointAngles=[{4}:{5}:{6}:{7}]", 
                            pose.x, pose.y, pose.z, pose.rHead, pose.jointAngle[0], pose.jointAngle[1], pose.jointAngle[2], pose.jointAngle[3]);
                    }
                }

                // add poses from .playback-file to command queue
                if (run)
                {
                    // append .playback to filename
                    string[] segments = filename.Split(".");
                    if (segments.Last<string>() != "playback")
                    {
                        filename = filename + ".playback";
                    }
                    // if file exists add poses to queue
                    try
                    {
                        DobotPose[] poses = xmlReader.getPoses(filename);
                        Enqueue(poses);
                    }
                    catch (System.IO.FileNotFoundException e)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("file '{0}' not found!", filename);
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                    run = false;
                }

            }
        }

        static void Enqueue(DobotPose[] poses)
        {
            foreach (DobotPose pose in poses)
            {
                Enqueue(pose);
            }
        }

        static UInt64 Enqueue(DobotPose pose)
        {
            PTPCmd pdbCmd;
            UInt64 cmdIndex = 0;
            WAITCmd waitCmd = new WAITCmd();

            pdbCmd.ptpMode = pose.style;
            pdbCmd.x = pose.x;
            pdbCmd.y = pose.y;
            pdbCmd.z = pose.z;
            pdbCmd.rHead = pose.r;
            while (true)
            {
                int ret = DobotDll.SetPTPCmd(ref pdbCmd, true, ref cmdIndex);
                if (ret == 0)
                    break;
            }
            while (true)
            {
                int ret = DobotDll.SetEndEffectorSuctionCup(pose.gripper, true, true, ref cmdIndex);
                if (ret == 0)
                    break;
            }
            while (true)
            {
                waitCmd.timeout = pose.pauseTime;
                int ret = DobotDll.SetWAITCmd(ref waitCmd, true, ref cmdIndex);
                if (ret == 0)
                    break;
            }

            return cmdIndex;
        }


        static void ShowErrorMsg(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
            Console.ForegroundColor = ConsoleColor.White;
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
        public uint pauseTime;
        public bool gripper;

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
            pauseTime = (uint)(float.Parse(row.Element("item_10").Value, numFormat) * 1000);
            gripper = int.Parse(row.Element("item_12").Value) == 1;
        }
    }

    class XMLReader
    {
        public DobotPose[] getPoses(string file)
        {
            XElement playback = XElement.Load(file);
            var elements = playback.Elements().ToList<XElement>();

            DobotPose[] poses = new DobotPose[elements.Count() - 2];
            for (int i = 2; i < elements.Count(); i++)
            {
                poses[i - 2] = new DobotPose(elements[i]);
            }

            return poses;
        }
    }
}
