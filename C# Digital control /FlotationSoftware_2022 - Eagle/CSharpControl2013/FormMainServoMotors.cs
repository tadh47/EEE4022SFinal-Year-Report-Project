using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace CSharpControl2013
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            SetupChart();           
        }

        #region DAQLite Drivers WARNING! Do not Change

        EagleDaq.EagleDaq DAQ = new EagleDaq.EagleDaq();
        string fileName = "C:\\ServoMotorLogs\\ServoMotorData.CSV";
        bool Logging = false;
        long LogSamples=0;
        int chartPeriod = 84;
        double XValue;
        int inputType = 1;                  // controls input type
        int count = 0;                      // Timer variable   

        #endregion

        // Global control variables
        double previous_Error = 0; // Initialising e(k-1) to zero
        double previous_ut_Output = 0; //Initialisng u(k-1) to zero
        double previous_vt_Output = 0; // Initialising v(k-1) to zero

        double yt_PlantOutput; // Level of water in the rougher tank @ ADC INPUT 0 (Output from rougher capacitive level sensor)
        double rt_SetPoint = 0; // %Setpoint of desired rougher water level from the operator using HMI @ ADC INPUT 1 initialised to zero
        double Gain = 1;   // Gain Default to one 
        double rt_Gain = 1;

        double Scavenger_level_percentage = 0;
        double Rougher_level_percentage = 0;
        double rougherValve_Voltage = 0;

        // Global constants
        double KP = 65.9; // PI controller KP proportional term
        double KI = 0.5402; // PI controller KI intergral term
        double Ts = 0.1; // Sampling time
        private void FormLoad(object sender, EventArgs e)   // Connect to ADC/DAC
        {                        
            try 
            {
                string DAQName;
                string SerialNumber;
                DAQ.ReadDAQ(out SerialNumber, out DAQName);
                textBoxDAQName.Text = DAQName;
                textBoxDAQSerial.Text = SerialNumber;              
            }
            catch
            {
                MessageBox.Show("uDAQ not connected", "Cannot read uDAQ",
                                       MessageBoxButtons.OK, MessageBoxIcon.Error);
            }             
        }
        
        // Interrupt for timer
        private void Timer_Tick(object sender, EventArgs e)
        {
            string WriteData = "";
            double Increment = Convert.ToDouble(timer1.Interval) / 1000;
            XValue = XValue+Increment;
            //double XValue = count * timer1.Interval;

            
            double TimeElapsed = XValue;
            textBoxTimeElapsed.Clear();
            textBoxTimeElapsed.AppendText(TimeElapsed.ToString("0.00"));
            WriteData = TimeElapsed.ToString("0.00"); // Record into text string for data logging

            double RougherOffSet = 0.093;
            yt_PlantOutput = DAQ.Input(0) + RougherOffSet;            // Read and Display yt @ ADC INPUT 0 and adding offset          
            Rougher_level_percentage = Math.Round(((yt_PlantOutput / 6.73) * 100), 1); // Converting level into percentage
            textBoxPlantOutput.Text = Rougher_level_percentage.ToString("0.000");     // Display yt
            WriteData += "," + Rougher_level_percentage.ToString("0.000");            // Record into text string for data logging
            chart1.Series["Rougher Level"].Points.AddXY(XValue, Rougher_level_percentage);
            

            string InputSignal = comboBoxInputSignal.Text;
            if (InputSignal == "EXTERNAL(DAQ-1)")
            {
               rt_SetPoint = DAQ.Input(1);           // Read and Setpoint @ ADC INPUT 1 
              rt_SetPoint = (rt_SetPoint / 8.556) * 100; // 0-10v into 0-100 percentage conversion
                if(rt_SetPoint<0)
                {
                    rt_SetPoint = 0;
                }
              //rt_SetPoint = 0; // testing since no DAQ input yet
            }
            else
            {
                try
                {
                    rt_SetPoint = rt_Gain + TimeElapsed * Convert.ToDouble(textBoxRamp.Text);
                }
                catch
                { }
            }


            textBoxSetPoint.Text = rt_SetPoint.ToString("0.000");                // Display Setpoint
            WriteData += "," + rt_SetPoint.ToString("0.000");
            chart1.Series["rt_SetPoint"].Points.AddXY(XValue, rt_SetPoint);


            double ScavengerOffSet = 0;
            double Scavenger_level = DAQ.Input(2) + ScavengerOffSet;     // Reading the scavenger level and adding an offset
            Scavenger_level_percentage = Math.Round(((Scavenger_level / 7.45) * 100), 1); // Converting level into percentage
            textBoxMotorVelocity.Text = Scavenger_level_percentage.ToString("0.000"); // Display Scavenger level 
            WriteData += "," + Scavenger_level_percentage.ToString("0.000");   // Record into text string for data logging
            chart1.Series["Scavenger Level"].Points.AddXY(XValue, Scavenger_level_percentage);
            
            

            //Controller Region          
            double Error = rt_SetPoint - Rougher_level_percentage;
           // Error = 1.2 * Error; // Calculating error for controller
            double ut_Output = previous_ut_Output + KP * Error - KP * previous_Error + KI * Ts * Error; //Controller output algorithm 

            previous_Error = Error;   // Storing error to previous error
            previous_ut_Output = ut_Output;  // Storing controller output to previous controller output

            double vt_Output = 0.873 * previous_vt_Output + 0.0004720* ut_Output; // Velocity tranfer function output
            previous_vt_Output = vt_Output; // storing previous output

            double r_Ouputflowrate = (Math.PI * Math.Pow(0.2244, 2)) * vt_Output; // Area * Velocity
            double outputflowrate = (0.000756 - r_Ouputflowrate) * -1;  // (Inputflowrate(Const)-Outputflowrate)*-1(negative since output)




            rougherValve_Voltage = 8 * Math.Pow(10, 10) * Math.Pow(outputflowrate, 3) + 1 * Math.Pow(10, 8) * Math.Pow(outputflowrate, 2) + 47361 * Math.Pow(outputflowrate, 1) + 7.3739;

            string ControlLoop = comboBoxControlLoop.Text;
            if (ControlLoop == "OPEN")
            { ut_Output = rt_SetPoint; }

            if (rougherValve_Voltage >= 10)  // Saturation of rougher valve voltage(0-10V)
            {
                rougherValve_Voltage = 10;
            }
            else if (rougherValve_Voltage <= 0)
            {
                rougherValve_Voltage = 0;
            }

            //if (Rougher_level_percentage>=95) // close rougher valve is water level is >95
           // {
             //   rougherValve_Voltage = 10;
           // } stops the resolving of the overflow by the operator

           
            DAQ.Output(0, rougherValve_Voltage);    // Send output to DAQ @0 (Rougher valve) 
            //double scavengerValve_Voltage = 8; // close scavenger rougher valve so not water is discharged
            //DAQ.Output(1, scavengerValve_Voltage);    // Send output to DAQ @0 (Scavenger valve) 
           // double ut_PlantInput = ut_Output;
            textBoxControllerOutput_ut.Text = rougherValve_Voltage.ToString("0.000");
            WriteData += "," + rougherValve_Voltage.ToString("0.000");
            chart1.Series["Rougher Valve"].Points.AddXY(XValue, rougherValve_Voltage);


            #region // FOR LOGGING DATA
            if (Logging == true)
            {
                // Write data into file 
                StreamWriter Datafile = new StreamWriter(fileName, true);
                Datafile.WriteLine(WriteData);
                Datafile.Close();
                // Show the sample count
                LogSamples++;
                textBoxLogSamples.Clear();
                textBoxLogSamples.Text = LogSamples.ToString();
            }


            if (XValue > 420)
            {
                timer1.Enabled = false;
            }

            #endregion
        }

        private void StartControl(object sender, EventArgs e)
        {            
            int interval;       // Enable and set timer            
            interval = Convert.ToInt16(textBoxInterval.Text);
            timer1.Interval = interval;
            buttonStartControl.Text = "CHART ON";
            textBoxInterval.Enabled = false;        // Disable appropriate text boxes
            //textBoxChartPeriod.Enabled = false;
            if (inputType == 0)
            {
                textBoxInputSignal.Enabled = false; 
                //textBoxStepLength.Enabled = false;
            }

            try
            {                
                timer1.Enabled = true;
            }
            catch
            {
                MessageBox.Show("DT9812 not connected", "Cannot read DT9812",
                                       MessageBoxButtons.OK, MessageBoxIcon.Error);
                timer1.Enabled = false;
            }

            SetupChart();      // Charts reset

           //chartPeriod = Convert.ToInt32(textBoxChartPeriod.Text) * 1000;      // Sets chart period

            count = 0;      // Reset counter

            // Initially Read Input from the TextBox
            try
            {
                rt_SetPoint = Convert.ToDouble(textBoxInputSignal.Text);
            }
            catch { }
            textBoxSetPoint.Text = rt_SetPoint.ToString("0.0");          // Display rt 
        }

        private void StopControl(object sender, EventArgs e)
        {            
            timer1.Enabled = false;     // Disable timer
            buttonStartControl.Text = "CHART OFF";
            textBoxInterval.Enabled = true;
            textBoxInputSignal.Enabled = true;
            //textBoxStepLength.Enabled = true;
            //textBoxChartPeriod.Enabled = true;

            //output.SetSingleValueAsVolts(0, 0);     // Write ut to DAC Output 0	            
            textBoxPlantOutput.Text = "0";          // Set output text to 0;
            yt_PlantOutput = 0;            
            count = 0;      // Reset counter
        }

        private void closeButton_Click(object sender, EventArgs e)
        {
            //output.SetSingleValueAsVolts(0, 0);     // Write ut to DAC Output 0	            
            textBoxPlantOutput.Text = "0";      // Set output text to 0;
            
            this.Close();
        }

        #region DATA LOGGING 

        private void Startlogging(object sender, EventArgs e)
        {
            int interval = timer1.Interval;
            string sInterval = interval.ToString();
            FileStream Datafile1;
            
            buttonLogging.Text = "LOGGING ON";
            Logging = true;

            textBoxLogSamples.BackColor = System.Drawing.ColorTranslator.FromWin32(-16776961);
            textBoxLogSamples.ForeColor = System.Drawing.ColorTranslator.FromWin32(-1 );

            // Recreate Create file              
            Datafile1 = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            Datafile1.Close();

            StreamWriter Datafile = new StreamWriter(fileName, true);
            Datafile.WriteLine("DATA LOGGER INTERVAL @ " + sInterval + "ms");
            Datafile.WriteLine("TIME(s),Rougher_level,r(t)_SetPoint,Scavenger_level,rougherValve_Voltage");
            Datafile.Close();           
        }

        private void StopLogging(object sender, EventArgs e)
        {
            buttonLogging.Text = "START LOGGING";
            Logging = false;
            LogSamples = 0;
            textBoxLogSamples.BackColor = System.Drawing.ColorTranslator.FromWin32(-1);
            textBoxLogSamples.ForeColor = System.Drawing.ColorTranslator.FromWin32(-16777216);
        }

        #endregion

        #region CHART SETUP
        private void button1_Click(object sender, EventArgs e)
        {
            SetupChart();
        }      
        public void SetupChart()
        {
            if (Convert.ToInt32(textBoxInterval.Text) < 20) textBoxInterval.Text = "20";        // Limit minimum time interval
            timer1.Interval = Convert.ToInt32(textBoxInterval.Text); // Setting timer interval

            XValue = 0;

            chart1.Series.Clear();                                   // Initialising chart components

            chart1.Series.Add("Rougher Level");                          // Add series for yt and rt
            chart1.Series.Add("rt_SetPoint");
            chart1.Series.Add("Rougher Valve");
            chart1.Series.Add("Scavenger Level");

            chart1.Series["Rougher Level"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine; // Set type of graph to line
            chart1.Series["rt_SetPoint"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            chart1.Series["Rougher Valve"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            chart1.Series["Scavenger Level"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            // Assign colours to series
            chart1.Series["Rougher Level"].Color = Color.Blue;
            chart1.Series["rt_SetPoint"].Color = Color.Red;
            chart1.Series["Rougher Valve"].Color = Color.Black;
            chart1.Series["Scavenger Level"].Color = Color.Brown;

            chart1.ChartAreas[0].AxisY.Maximum = 100;                // Set Axis limits
            chart1.ChartAreas[0].AxisY.Minimum = -10;           
            chart1.ChartAreas[0].AxisY.MajorTickMark.Interval = 10;  // Settings for grids  setting on 1
            chart1.ChartAreas[0].AxisY.MajorGrid.Interval = 10;
            chart1.ChartAreas[0].AxisY.MinorGrid.Enabled = true;
            chart1.ChartAreas[0].AxisY.MinorGrid.Interval = 5;
            chart1.ChartAreas[0].AxisY.MinorGrid.LineColor = Color.LightGray;
            chart1.ChartAreas[0].AxisY.LabelStyle.Interval = 10;
            chart1.ChartAreas[0].AxisY.Title = "Values";

            // X-Axis Settings
            chart1.ChartAreas[0].AxisX.MinorGrid.Enabled = true;
            chart1.ChartAreas[0].AxisX.MajorGrid.Enabled = true;
            chart1.ChartAreas[0].AxisX.MinorGrid.Interval = 1;
            chart1.ChartAreas[0].AxisX.MajorGrid.Interval = 5;
            chart1.ChartAreas[0].AxisX.MinorGrid.LineColor = Color.LightGray;
            chart1.ChartAreas[0].AxisX.MajorGrid.LineColor = Color.Black;
            chart1.ChartAreas[0].AxisX.LabelStyle.Interval = 5;

            chart1.ChartAreas[0].AxisX.Maximum = chartPeriod;
            chart1.ChartAreas[0].AxisX.Minimum = 0;


            //chart1.Series.Clear();      // Reset chart
            //chart1.Series.Add("yt");
            //chart1.Series.Add("rt");
            //chart1.Series["yt"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            //chart1.Series["rt"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            //chart1.Series["yt"].Color = Color.Blue;
            //chart1.Series["rt"].Color = Color.Red;
            //chart1.ChartAreas[0].AxisX.Maximum = chartPeriod;
            //chart1.ChartAreas[0].AxisX.Minimum = 0;


            count = 0;      // Reset counter
        }
        #endregion

        private void ChangeStep(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                try
                {
                    rt_SetPoint = Convert.ToDouble(textBoxInputSignal.Text);                   
                }
                catch { }                                   
                textBoxSetPoint.Text = rt_SetPoint.ToString("0.0");          // Display rt  
            }
        }

        // Change Gain
        private void ChangeSystemGain(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                Gain = Convert.ToDouble(textBoxSetGain.Text);                              
            }
        }

		private void ApplyInput(object sender, EventArgs e)
		{
			try
			{
				rt_Gain = Convert.ToDouble(textBoxInputSignal.Text);
			}
			catch { }
			textBoxSetPoint.Text = rt_Gain.ToString("0.0");
		}

        private void chart1_Click(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }
    }
}
