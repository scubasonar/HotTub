using System;
using System.Threading;
using System.IO.Ports;
using System.Text;

using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;

using GHIElectronics.NETMF.Hardware;
using GHIElectronics.NETMF.FEZ;

namespace HotTubMaster
{
    public class Program
    {
        static PWM MotorLeft = new PWM((PWM.Pin)FEZ_Pin.PWM.Di10); // hooked up to a MOSFET
        static PWM MotorRight = new PWM((PWM.Pin)FEZ_Pin.PWM.Di9); // hooked up to a MOSFET
        static PWM TubLight = new PWM((PWM.Pin)FEZ_Pin.PWM.Di8);// hooked up to a transistor
        static AnalogIn distanceSensor = new AnalogIn((AnalogIn.Pin)FEZ_Pin.AnalogIn.An0); // MAX sensor 
        static OutputPort LED = new OutputPort((Cpu.Pin)FEZ_Pin.Digital.Di13, false); // LED on the Fez Panda
        static SerialPort radio = new SerialPort("COM1", 9600); // Xbee XSC radio 
        static string radioBuffer = ""; // incoming buffer

        // idle = sitting around waiting for someone to walk past the sculpture
        // live = sculpture just got triggered
        // config = IR remote command detected, so wait for more commands 
        // test = full system test, operate each component in sequence 
        enum ProgramStates {idle = 0, live = 1 , config = 2, test = 3};
        static ProgramStates currentState = ProgramStates.test;  // always run the test first when the sculpture turns on so we know it's OK! 

        public static void Main() 
        {
            radio.DataReceived += new SerialDataReceivedEventHandler(radio_DataReceived);
            radio.Open();
           
            // Blink board LED
            bool ledState = false;
            
            OutputPort led = new OutputPort((Cpu.Pin)FEZ_Pin.Digital.LED, ledState);
            TubLight.Set(true); // turn it off
            
            while (true)
            {
                switch (currentState)
                {
                    case ProgramStates.idle:
                        double dist = getDistance(distanceSensor);
                        if ((dist < 1) && (currentState == ProgramStates.idle))
                        {
                            radio.Write(new byte[] { (byte)'$', (byte)'a', (byte)'c', (byte)'t', (byte)'i', (byte)'o', (byte)'n', (byte)'\r' }, 0, 8);
                            Thread.Sleep(20);
                            currentState = ProgramStates.live;
                        }
                        break;
                    case ProgramStates.live:
                        playShow();
                        if(currentState == ProgramStates.live) // make sure the state didn't change during the show
                            currentState = ProgramStates.idle;
                        break;
                    case ProgramStates.config:
                        if (currentState == ProgramStates.config)
                            currentState = ProgramStates.idle;
                        break;
                    case ProgramStates.test:
                        doTest();
                        if (currentState == ProgramStates.test)
                            currentState = ProgramStates.idle;
                        break;
                }
                

            }
        }

        static void radio_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort radio = sender as SerialPort;
            int cnt = radio.BytesToRead;
            byte[] rx = new byte[cnt];

            radio.Read(rx, 0, cnt);
            for (int i = 0; i < cnt; i++)
            {
                if (rx[i] == '$')
                    radioBuffer = "$";
                else
                    radioBuffer += (char)rx[i];
                if(rx[i] == '\r')
                {
                    parse(radioBuffer);
                }
            }

            if (radioBuffer.Length > 255)
                radioBuffer = "";
        }

        static void parse(string buffer)
        {
            if (buffer.IndexOf("TEST", 0) > 0)
                currentState = ProgramStates.test;
            else if (buffer.IndexOf("test", 0) > 0)
                currentState = ProgramStates.test;
            if (buffer.IndexOf("ACTION", 0) > 0)
                currentState = ProgramStates.live;
            else if (buffer.IndexOf("action", 0) > 0)
                currentState = ProgramStates.live;
        }
        // gets the distance from a MaxSonar ultrasonic sensor and returns the value
        // in feet. 
        public static double getDistance(AnalogIn sensor)
        {
            uint distance = 0;
            for (int i = 0; i < 200; i++)
                distance += (uint)sensor.Read();
            distance /= 200;

            double distInches = distance * (3300.0 / 512);
            Debug.Print("distance: " + (distInches / 144).ToString("F4") + "");
            
            return distInches / 144.0;

        }

        public static void playShow()
        {
            TubLight.Set(false);
            for (int i = 0; i < 1; i++)
            {
                MotorLeft.SetPulse(1000000, 300000);
                MotorRight.SetPulse(1000000, 300000);
                Thread.Sleep(5000);
                MotorLeft.Set(false);
                MotorRight.Set(false);
                Thread.Sleep(1000);
            }
            TubLight.Set(true);
        }

        public static void doTest()
        {
            int i = 0;

            for (i = 0; i < 10; i++)
            {
                MotorLeft.SetPulse(1000000, 200000);
                Thread.Sleep(100);
                MotorLeft.Set(false);
                Thread.Sleep(100);
                MotorRight.SetPulse(1000000, 200000);
                Thread.Sleep(100);
                MotorRight.Set(false);
                Thread.Sleep(250);
            }

            MotorLeft.Set(false);
            MotorRight.Set(false);

            for (i = 0; i < 5; i++)
            {
                TubLight.Set(true);
                Thread.Sleep(100);
                TubLight.Set(false);
                Thread.Sleep(250);
            }

            TubLight.Set(true);
        }
    }
}
