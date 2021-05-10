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
        static bool isConnected = false;
        static byte isJoint = (byte)0;
        static JogCmd currentCmd;
        static Pose pose = new Pose();
        static System.Timers.Timer posTimer = new System.Timers.Timer();
        static HOMECmd homeCmd = new HOMECmd();
        static ulong queuedCmdIndex = 0;

        static void Main(string[] args)
        {

            // connect and initialize
            Console.WriteLine("Connecting to Dobot...");
            StringBuilder fwType = new StringBuilder(60);
            StringBuilder version = new StringBuilder(60);

            int ret = DobotDll.ConnectDobot("", 115200, fwType, version);
            if (ret == (int)DobotConnect.DobotConnect_NoError)
            {
                Console.WriteLine("Connected successfully - type={0}, version={1}", fwType, version);
                isConnected = true;
            }
            else if (ret == (int)DobotConnect.DobotConnect_NotFound)
            {
                Console.WriteLine("DobotConnect_NotFound");
                return;
            }
            else if (ret == (int)DobotConnect.DobotConnect_NotFound)
            {
                Console.WriteLine("DobotConnect_Occupied");
                return;
            }

            // set home parameters
            HOMEParams home = new HOMEParams();
            home.x = 250;
            home.y = 0;
            home.z = 85;
            home.r = 0;
            DobotDll.SetHOMEParams(ref home, true, ref queuedCmdIndex);

            WaitForUserInput();
            DobotDll.DisconnectDobot();

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
                    if (setFilename && filename == "")
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
                ptp(pose.style, pose.x, pose.y, pose.z, pose.r, pose.gripper);
            }
        }

        static UInt64 ptp(byte style, float x, float y, float z, float r, bool gripper)
        {
            PTPCmd pdbCmd;
            UInt64 cmdIndex = 0;

            pdbCmd.ptpMode = style;
            pdbCmd.x = x;
            pdbCmd.y = y;
            pdbCmd.z = z;
            pdbCmd.rHead = r;
            while (true)
            {
                int ret = DobotDll.SetPTPCmd(ref pdbCmd, true, ref cmdIndex);
                if (ret == 0)
                    break;
            }
            while (true)
            {
                int ret = DobotDll.SetEndEffectorSuctionCup(gripper, true, true, ref cmdIndex);
                if (ret == 0)
                    break;
            }

            return cmdIndex;
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
        public float pauseTime;
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
            x = float.Parse(row.Element("item_2").Value, numFormat);
            y = float.Parse(row.Element("item_3").Value, numFormat);
            z = float.Parse(row.Element("item_4").Value, numFormat);
            r = float.Parse(row.Element("item_5").Value, numFormat);
            pauseTime = float.Parse(row.Element("item_10").Value, numFormat);
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
