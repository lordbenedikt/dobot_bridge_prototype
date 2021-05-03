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
        static void Main(string[] args)
        {
            var xmlReader = new XMLReader();

            bool isConnected = false;

            byte isJoint = (byte)0;
            JogCmd currentCmd;
            Pose pose = new Pose();
            System.Timers.Timer posTimer = new System.Timers.Timer();
            HOMECmd homeCmd = new HOMECmd();
            ulong queuedCmdIndex = 0;

            //initialize
            StringBuilder fwType = new StringBuilder(60);
            StringBuilder version = new StringBuilder(60);

            ///connect dobot
            int ret = DobotDll.ConnectDobot("", 115200, fwType, version);
            if (ret == (int)DobotConnect.DobotConnect_NoError)
            {
                Console.WriteLine("Connected successfully - type={0}, version={1}", fwType, version);
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

            isConnected = true;

            HOMEParams home = new HOMEParams();
            home.x = 250;
            home.y = 0;
            home.z = 85;
            home.r = 0;
            DobotDll.SetHOMEParams(ref home, true, ref queuedCmdIndex);


            string input = "";
            while (input != "exit")
            {
                input = Console.ReadLine();

                if (isConnected)
                {
                    if (input == "moveA")
                    {
                        DobotPose[] pos = xmlReader.getPoses("test.playback");
                        Console.WriteLine("{0}  {1}  {2}  {3}  {4}", (byte)1, pos[0].x, pos[0].y, pos[0].z, pos[0].r);
                        ptp((byte)1, pos[0].x, pos[0].y, pos[0].z, pos[0].r, pos[0].gripper);
                    }

                    if (input == "moveB")
                    {
                        ptp((byte)1, 30.99f, 106.4f, 0.99f, 0, false);
                    }

                    if (input == "enqueue")
                    {
                        DobotPose[] poses = xmlReader.getPoses("testrun.playback");
                        enqueue(poses);
                    }

                    if (input == "getPose")
                    {
                        DobotDll.GetPose(ref pose);
                        string posAsString = String.Format("x:{0}   y:{1}   z:{2}   rHead:{3}   jointAngle:{4}\n", pose.x, pose.y, pose.z, pose.rHead, pose.jointAngle);
                        Console.WriteLine(posAsString);
                    }

                    if (input == "home")
                    {
                        DobotDll.SetHOMECmd(ref homeCmd, false, ref queuedCmdIndex);
                    }

                    if (input == "clearQueue")
                    {
                        DobotDll.SetQueuedCmdClear();
                    }
                }

            }

            DobotDll.DisconnectDobot();



        }

        public static void enqueue(DobotPose[] poses)
        {
            foreach(DobotPose pose in poses)
            {
                ptp(pose.style, pose.x, pose.y, pose.z, pose.r, pose.gripper);
            }
        }

        public static UInt64 ptp(byte style, float x, float y, float z, float r, bool gripper)
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

    public class DobotPose
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

    public class XMLReader
    {
        public DobotPose[] getPoses(string file)
        {
            XElement playback = XElement.Load(file);
            var elements = playback.Elements().ToList<XElement>();

            DobotPose[] poses = new DobotPose[elements.Count() - 2];

            for (int i = 2; i < elements.Count(); i++)
            {
                poses[i-2] = new DobotPose(elements[i]);
            }

            return poses;

        }
    }
}
