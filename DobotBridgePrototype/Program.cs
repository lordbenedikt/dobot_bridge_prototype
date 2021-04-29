using System;
using System.Text;
using DobotBridgePrototype.CPlusDll;

namespace DobotBridgePrototype
{
    class Program
    {


        static void Main(string[] args)
        {
            bool isConnected = false;

            byte isJoint = (byte)0;
            JogCmd currentCmd;
            Pose pose = new Pose();
            System.Timers.Timer posTimer = new System.Timers.Timer();

            string input = "";
            while (input != "exit")
            {
                input = Console.ReadLine();

                if (input == "connect")
                {
                    StringBuilder fwType = new StringBuilder(60);
                    StringBuilder version = new StringBuilder(60);

                    ///connect dobot
                    int ret = DobotDll.ConnectDobot("", 115200, fwType, version);
                    if (ret != (int)DobotConnect.DobotConnect_NoError)
                    {
                        Console.WriteLine("Connect error");
                        return;
                    }
                    Console.WriteLine("Connect success");

                    isConnected = true;
                }
                
                if (input == "disconnect")
                {
                    DobotDll.DisconnectDobot();
                    isConnected = false;
                    Console.WriteLine("Disconnected");
                }

                if (isConnected)
                {
                    if (input == "moveA")
                    {
                        ptp((byte)1, 67.99f, 216.4f, -27.99f, 0);
                    }

                    if (input == "moveB")
                    {
                        ptp((byte)1, 30.99f, 106.4f, 0.99f, 0);
                    }

                    if (input == "getPose")
                    {
                        DobotDll.GetPose(ref pose);
                        string posAsString = String.Format("x:{0}   y:{1}   z:{2}   rHead:{3}   jointAngle:{4}\n", pose.x, pose.y, pose.z, pose.rHead, pose.jointAngle);
                        Console.WriteLine(posAsString);
                    }
                }

            }

            DobotDll.DisconnectDobot();

            UInt64 ptp(byte style, float x, float y, float z, float r)
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

                return cmdIndex;
            }

        }

        private void StartDobot()
        {

        }
    }
}
