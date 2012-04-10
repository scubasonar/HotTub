using System;
using System.Threading;
using System.IO.Ports;
using System.Text;

using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;

using GHIElectronics.NETMF.Hardware;
using GHIElectronics.NETMF.FEZ;
using GHIElectronics.NETMF.Hardware.LowLevel;

namespace HotTubMaster
{
    public class Program
    {
        static PWM motorLeft = new PWM((PWM.Pin)FEZ_Pin.PWM.Di10); // hooked up to a MOSFET
        static PWM motorRight = new PWM((PWM.Pin)FEZ_Pin.PWM.Di9); // hooked up to a MOSFET
        static PWM tubLight = new PWM((PWM.Pin)FEZ_Pin.PWM.Di8);// hooked up to a transistor
        static AnalogIn distanceSensor = new AnalogIn((AnalogIn.Pin)FEZ_Pin.AnalogIn.An0); // MAX sensor 
        static AnalogIn batteryVoltage = new AnalogIn((AnalogIn.Pin)FEZ_Pin.AnalogIn.An1);

        static OutputPort pcbLed = new OutputPort((Cpu.Pin)FEZ_Pin.Digital.Di13, false); // LED on the Fez Panda
        
        static SerialPort radio = new SerialPort("COM1", 9600); // Xbee XSC radio 
        static OutputPort radioPower;
        static string radioBuffer = ""; // incoming buffer
        static DateTime lastComms = new DateTime(2010, 1, 1, 1, 1, 1);

        // idle = sitting around waiting for someone to walk past the sculpture
        // live = sculpture just got triggered
        // config = IR remote command detected, so wait for more commands 
        // test = full system test, operate each component in sequence 
        enum ProgramStates {idle = 0, live = 1 , config = 2, test = 3};
        static ProgramStates currentState = ProgramStates.test;  // always run the test first when the sculpture turns on so we know it's OK! 

        public static void Main() 
        {
            double batV = 0;
            double dist = 1000;
            string debugMsg = "";
            RealTimeClock.SetTime(new DateTime(2010, 1, 1, 1, 1, 1));
           

            radioPower = new OutputPort((Cpu.Pin)FEZ_Pin.Digital.Di5, true); // /Sleep pin on xbee
            radio.DataReceived += new SerialDataReceivedEventHandler(radio_DataReceived);
            radio.Open();
           
            // Blink board LED
            bool ledState = false;
            
            OutputPort led = new OutputPort((Cpu.Pin)FEZ_Pin.Digital.LED, ledState);
            tubLight.Set(true); // turn it off
            Thread.Sleep(10000);// wait for programming just incase
            while (true)
            {
                // make sure the battery voltage is high enough so we don't destroy the battery! 
                batV = batteryVoltage.Read() * (3.3 / 1024) / .25;
                Debug.Print(batV.ToString());
                if (batV < 12)
                {
                    radioPower.Write(true);

                    tubLight.Set(false);
                    Thread.Sleep(100);
                    tubLight.Set(true);
                    Thread.Sleep(100);
                    tubLight.Set(false);
                    Thread.Sleep(100);
                    tubLight.Set(true);

                    RealTimeClock.SetAlarm(RealTimeClock.GetTime().AddSeconds(10));
                    Power.Hibernate(Power.WakeUpInterrupt.RTCAlarm);

                }

                else
                {
                    switch (currentState)
                    {
                        case ProgramStates.idle:
                            radioPower.Write(false);
                            dist = getDistance(distanceSensor);
                            if ((dist < 3) && (currentState == ProgramStates.idle))
                            {
                                lastComms = RealTimeClock.GetTime();
                                // send out a wakeup call for around 11 seconds to get the neighbors out of sleep mode and ready to sync up
                                for (int i = 0; i < 250; i++)
                                {
                                    radio.Write(new byte[] { (byte)'$'}, 0, 1);
                                    Thread.Sleep(10);
                                }
                                debugMsg = "dist: " + dist.ToString("F2") + ", bat: " + batV.ToString("F2") + "\r";
                                radio.Write(UTF8Encoding.UTF8.GetBytes(debugMsg), 0, debugMsg.Length);
                                radio.Write(new byte[] { (byte)'$', (byte)'a', (byte)'c', (byte)'t', (byte)'i', (byte)'o', (byte)'n', (byte)'\r' }, 0, 8);
                                Thread.Sleep(400);
                                currentState = ProgramStates.live;
                                radioPower.Write(true);
                            }

                            else
                            {
                                Thread.Sleep(100); // give it a moment to listen for comms.

                                led.Write(true);
                                pcbLed.Write(true);
                                Thread.Sleep(100);
                                pcbLed.Write(false);
                                led.Write(false);
                                Thread.Sleep(50);
                                TimeSpan timeDif = RealTimeClock.GetTime() - lastComms;

                                int mins = (timeDif.Days * 1440) + (timeDif.Hours * 60) + timeDif.Minutes;
                                if ((currentState == ProgramStates.idle) && mins > 1)
                                {
                                    radioPower.Write(true);
                                    RealTimeClock.SetAlarm(RealTimeClock.GetTime().AddSeconds(2));
                                    Power.Hibernate(Power.WakeUpInterrupt.RTCAlarm);
                                }
                                else
                                {
                                    Thread.Sleep(500);
                                }
                            }
                            break;
                        case ProgramStates.live:
                            playShow();
                            debugMsg = "dist: " + dist.ToString("F2") + ", bat: " + batV.ToString("F2") + "\r";
                            radio.Write(UTF8Encoding.UTF8.GetBytes(debugMsg), 0, debugMsg.Length);
                            RealTimeClock.SetAlarm(RealTimeClock.GetTime().AddSeconds(30));
                            Power.Hibernate(Power.WakeUpInterrupt.RTCAlarm);
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
        }

        static void radio_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {

            SerialPort radio = sender as SerialPort;
            int cnt = radio.BytesToRead;
            byte[] rx = new byte[cnt];
            lastComms = RealTimeClock.GetTime();
            
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
            {
                currentState = ProgramStates.live;
            }
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
            double multiplier = 0;
            tubLight.Set(false);
            Thread.Sleep(3000);
            
            // 30-40 for left, 40-50 for right
            for (int i = 40; i < 50; i++)
            {
               multiplier = Microsoft.SPOT.Math.Sin(i) / 1000.0;
                motorLeft.SetPulse(1000000, (uint)(700000 * multiplier));
                motorRight.SetPulse(1000000, (uint)(700000 * multiplier));
                //multiplier = Microsoft.SPOT.Math.Sin(i) / 1000.0;
                Thread.Sleep(2000);
             }
              

            /*
            for (int i = 200000; i < 700000; i += 100)
            {
                motorLeft.SetPulse(1000000, (uint)i);
                motorRight.SetPulse(1000000, (uint)i);
                Thread.Sleep(5);
            }
             */
               Thread.Sleep(5000);
            
            
               for (int i = 50; i > 40; i--)
                {
                    multiplier = Microsoft.SPOT.Math.Sin(i) / 1000.0;
                    motorLeft.SetPulse(1000000, (uint)(700000 * multiplier));
                    motorRight.SetPulse(1000000, (uint)(700000 * multiplier));
                    //multiplier = Microsoft.SPOT.Math.Sin(i) / 1000.0;
                    Thread.Sleep(2000);
                }
            /*
                for (int i = 700000; i > 200000; i -= 100)
                {
                    motorLeft.SetPulse(1000000, (uint)i);
                    motorRight.SetPulse(1000000, (uint)i);
                    Thread.Sleep(5);
                }
             */
                motorLeft.Set(false);
                motorRight.Set(false);
                Thread.Sleep(10000);
                tubLight.Set(true);
        }

        public static void doTest()
        {
            int i = 0;

            for (i = 0; i < 10; i++)
            {
                motorLeft.SetPulse(1000000, 200000);
                Thread.Sleep(100);
                motorLeft.Set(false);
                Thread.Sleep(100);
                motorRight.SetPulse(1000000, 200000);
                Thread.Sleep(100);
                motorRight.Set(false);
                Thread.Sleep(250);
            }

            motorLeft.Set(false);
            motorRight.Set(false);

            for (i = 0; i < 5; i++)
            {
                tubLight.Set(true);
                Thread.Sleep(100);
                tubLight.Set(false);
                Thread.Sleep(250);
            }

            tubLight.Set(true);
        }
    }
}
