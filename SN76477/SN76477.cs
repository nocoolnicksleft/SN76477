using System;
using System.IO;

/*****************************************************************************

Texas Instruments SN76477 emulator

authors: Derrick Renaud - info
         Zsolt Vasvari  - software

(see sn76477.h for details)

Notes:
    * All formulas were derived by taking measurements of a real device,
      then running the data sets through the numerical analysis
      application at http://zunzun.com to come up with the functions.

Known issues/to-do's:
    * VCO
        * confirm value of VCO_MAX_EXT_VOLTAGE, VCO_TO_SLF_VOLTAGE_DIFF
          VCO_CAP_VOLTAGE_MIN and VCO_CAP_VOLTAGE_MAX
        * confirm value of VCO_MIN_DUTY_CYCLE
        * get real formulas for VCO cap charging and discharging
        * get real formula for VCO duty cycle
        * what happens if no vco_res
        * what happens if no vco_cap

    * Attack/Decay
        * get real formulas for a/d cap charging and discharging

*****************************************************************************/


/*****************************************************************************
  Ported to C# by Bjoern Seip nocoolnicksleft@gmail.com
*****************************************************************************/


/*****************************************************************************
 *
 *  State structure
 *
 *****************************************************************************/
namespace SN76477
{

    public enum MixerMode
    {
        VCO = 0,
        SLF = 1,
        NOISE = 2,
        VCO_NOISE = 3,
        SLF_NOISE = 4,
        SLF_VCO_NOISE = 5,
        SLF_VCO = 6,
        INHIBIT = 7
    }

    public enum EnvelopeMode
    {
        VCO = 0,
        ONESHOT = 1,
        MIXER = 2,
        VCO_ALTERNATING = 3
    }

    [Serializable()]
    public class SN76477 
    {
        UInt32 _inhibit;
        UInt32 _envelope_mode;
        UInt32 _vco_mode;
        UInt32 _mixer_mode;

        double _one_shot_res;
        double _one_shot_res_var;
        double _one_shot_res_var_max;
        double _one_shot_cap;
        UInt32 _one_shot_cap_voltage_ext;

        double _slf_res;
        double _slf_res_var;
        double _slf_res_var_max;
        double _slf_cap;
        UInt32 _slf_cap_voltage_ext;

        double _vco_voltage;
        double _vco_res;
        double _vco_res_var;
        double _vco_res_var_max;
        double _vco_cap;
        UInt32 _vco_cap_voltage_ext;

        double _noise_clock_res;
        double _noise_clock_res_var;
        double _noise_clock_res_var_max;
        UInt32 _noise_clock_ext;
        UInt32 _noise_clock;

        double _noise_filter_res;
        double _noise_filter_res_var;
        double _noise_filter_res_var_max;
        double _noise_filter_cap;
        UInt32 _noise_filter_cap_voltage_ext;

        double _attack_res;
        double _attack_res_var;
        double _attack_res_var_max;
        double _decay_res;
        double _decay_res_var;
        double _decay_res_var_max;
        double _attack_decay_cap;
        UInt32 _attack_decay_cap_voltage_ext;

        double _amplitude_res;
        double _feedback_res;
        double _pitch_voltage;

        /* chip's internal state */
        double _one_shot_cap_voltage;		/* voltage on the one-shot cap */
        UInt32 _one_shot_running_ff;			/* 1 = one-shot running, 0 = stopped */

        double _slf_cap_voltage;				/* voltage on the SLF cap */
        UInt32 _slf_out_ff;					/* output of the SLF */

        double _vco_cap_voltage;				/* voltage on the VCO cap */
        UInt32 _vco_out_ff;					/* output of the VCO */
        UInt32 _vco_alt_pos_edge_ff;			/* keeps track of the # of positive edges for VCO Alt envelope */

        double _noise_filter_cap_voltage;	/* voltage on the noise filter cap */
        UInt32 _real_noise_bit_ff;			/* the current noise bit before filtering */
        UInt32 _filtered_noise_bit_ff;		/* the noise bit after filtering */
        UInt32 _noise_gen_count;				/* noise freq emulation */

        double _attack_decay_cap_voltage;	/* voltage on the attack/decay cap */

        UInt32 _rng;							/* current value of the random number generator */

        /* others */
        string _name;
        
        UInt32 sample_rate; 					/* from Machine->sample_rate */

        BinaryWriter file;

        public static double RES_K(double res)
        {
            return ((double)(res) * 1e3);
        }

        public static double RES_M(double res)
        {
            return ((double)(res) * 1e6);
        }

        public static double CAP_U(double cap)
        {
            return ((double)(cap) * 1e-6);
        }

        public static double CAP_N(double cap)
        {
            return ((double)(cap) * 1e-9);
        }

        public static double CAP_P(double cap)
        {
            return ((double)(cap) * 1e-12);
        }


        /*****************************************************************************
         *
         *  Debugging
         *
         *****************************************************************************/

        void LOG(int n, string x)
        {
            System.Diagnostics.Debug.WriteLine(x);
        }

        /*****************************************************************************
         *
         *  Constants
         *
         *****************************************************************************/
        public const double EXTERNAL_VOLTAGE_DISCONNECT = (-1.0);

        const double ONE_SHOT_CAP_VOLTAGE_MIN = (0);			/* the voltage at which the one-shot starts from (measured) */
        const double ONE_SHOT_CAP_VOLTAGE_MAX = (2.5);		/* the voltage at which the one-shot finishes (measured) */
        const double ONE_SHOT_CAP_VOLTAGE_RANGE = (ONE_SHOT_CAP_VOLTAGE_MAX - ONE_SHOT_CAP_VOLTAGE_MIN);

        const double SLF_CAP_VOLTAGE_MIN = (0.33);		/* the voltage at the bottom peak of the SLF triangle wave (measured) */
        const double SLF_CAP_VOLTAGE_MAX = (2.37);		/* the voltage at the top peak of the SLF triangle wave (measured) */
        const double SLF_CAP_VOLTAGE_RANGE = (SLF_CAP_VOLTAGE_MAX - SLF_CAP_VOLTAGE_MIN);

        const double VCO_MAX_EXT_VOLTAGE = (2.35);		/* the external voltage at which the VCO saturates and produces no output,
                                                   also used as the voltage threshold for the SLF */
        const double VCO_TO_SLF_VOLTAGE_DIFF = (0.35);
        const double VCO_CAP_VOLTAGE_MIN = (SLF_CAP_VOLTAGE_MIN);	/* the voltage at the bottom peak of the VCO triangle wave */
        const double VCO_CAP_VOLTAGE_MAX = (SLF_CAP_VOLTAGE_MAX + VCO_TO_SLF_VOLTAGE_DIFF);	/* the voltage at the bottom peak of the VCO triangle wave */
        const double VCO_CAP_VOLTAGE_RANGE = (VCO_CAP_VOLTAGE_MAX - VCO_CAP_VOLTAGE_MIN);
        const double VCO_DUTY_CYCLE_50 = (5.0);		/* the high voltage that produces a 50% duty cycle */
        const double VCO_MIN_DUTY_CYCLE = (18);		/* the smallest possible duty cycle, in % */

        double NOISE_MIN_CLOCK_RES = RES_K(10);	/* the maximum resistor value that still produces a noise (measured) */
        double NOISE_MAX_CLOCK_RES = RES_M(3.3);	/* the minimum resistor value that still produces a noise (measured) */
        const double NOISE_CAP_VOLTAGE_MIN = (0);			/* the minimum voltage that the noise filter cap can hold (measured) */
        const double NOISE_CAP_VOLTAGE_MAX = (5.0);		/* the maximum voltage that the noise filter cap can hold (measured) */
        const double NOISE_CAP_VOLTAGE_RANGE = (NOISE_CAP_VOLTAGE_MAX - NOISE_CAP_VOLTAGE_MIN);
        const double NOISE_CAP_HIGH_THRESHOLD = (3.35);		/* the voltage at which the filtered noise bit goes to 0 (measured) */
        const double NOISE_CAP_LOW_THRESHOLD = (0.74);		/* the voltage at which the filtered noise bit goes to 1 (measured) */

        const double AD_CAP_VOLTAGE_MIN = (0);	/* the minimum voltage the attack/decay cap can hold (measured) */
        const double AD_CAP_VOLTAGE_MAX = (4.44);	/* the minimum voltage the attack/decay cap can hold (measured) */
        const double AD_CAP_VOLTAGE_RANGE = (AD_CAP_VOLTAGE_MAX - AD_CAP_VOLTAGE_MIN);

        const double OUT_CENTER_LEVEL_VOLTAGE = (2.57);	/* the voltage that gets outputted when the volumne is 0 (measured) */
        const double OUT_HIGH_CLIP_THRESHOLD = (3.51);		/* the maximum voltage that can be put out (measured) */
        const double OUT_LOW_CLIP_THRESHOLD = (0.715);		/* the minimum voltage that can be put out (measured) */

        /* gain factors for OUT voltage in 0.1V increments (measured) */
        double[] out_pos_gain =
{
	0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.01,	 /* 0.0 - 0.9V */
	0.03, 0.11, 0.15, 0.19, 0.21, 0.23, 0.26, 0.29, 0.31, 0.33,  /* 1.0 - 1.9V */
	0.36, 0.38, 0.41, 0.43, 0.46, 0.49, 0.52, 0.54, 0.57, 0.60,  /* 2.0 - 2.9V */
	0.62, 0.65, 0.68, 0.70, 0.73, 0.76, 0.80, 0.82, 0.84, 0.87,  /* 3.0 - 3.9V */
	0.90, 0.93, 0.96, 0.98, 1.00							 	 /* 4.0 - 4.4V */
};

        double[] out_neg_gain =
{
	 0.00,  0.00,  0.00,  0.00,  0.00,  0.00,  0.00,  0.00,  0.00, -0.01,  /* 0.0 - 0.9V */
	-0.02, -0.09, -0.13, -0.15, -0.17, -0.19, -0.22, -0.24, -0.26, -0.28,  /* 1.0 - 1.9V */
	-0.30, -0.32, -0.34, -0.37, -0.39, -0.41, -0.44, -0.46, -0.48, -0.51,  /* 2.0 - 2.9V */
	-0.53, -0.56, -0.58, -0.60, -0.62, -0.65, -0.67, -0.69, -0.72, -0.74,  /* 3.0 - 3.9V */
	-0.76, -0.78, -0.81, -0.84, -0.85									   /* 4.0 - 4.4V */
};



        /*****************************************************************************
         *
         *  Max/min
         *
         *****************************************************************************/

        static double max(double a, double b)
        {
            return (a > b) ? a : b;
        }


        static double min(double a, double b)
        {
            return (a < b) ? a : b;
        }



        /*****************************************************************************
         *
         *  Functions for computing frequencies, voltages and similar values based
         *  on the hardware itself.  Do NOT put anything emulation specific here,
         *  such as calculations based on sample_rate.
         *
         *****************************************************************************/

        double compute_one_shot_cap_charging_rate() /* in V/sec */
        {
            /* this formula was derived using the data points below

             Res (kohms)  Cap (uF)   Time (millisec)
                 47         0.33         11.84
                 47         1.0          36.2
                 47         1.5          52.1
                 47         2.0          76.4
                100         0.33         24.4
                100         1.0          75.2
                100         1.5         108.5
                100         2.0         158.4
            */

            double ret = 0;

            if (((this._one_shot_res + this._one_shot_res_var)> 0) && (this._one_shot_cap > 0))
            {
                ret = ONE_SHOT_CAP_VOLTAGE_RANGE / (0.8024 * (this._one_shot_res + this._one_shot_res_var) * this._one_shot_cap + 0.002079);
            }
            else if (this._one_shot_cap > 0)
            {
                /* if no resistor, there is no current to charge the cap,
                   effectively making the one-shot time effectively infinite */
                ret = +1e-30;
            }
            else if ((this._one_shot_res + this._one_shot_res_var) > 0)
            {
                /* if no cap, the voltage changes extremely fast,
                   effectively making the one-shot time 0 */
                ret = +1e+30;
            }

            return ret;
        }


        double compute_one_shot_cap_discharging_rate() /* in V/sec */
        {
            /* this formula was derived using the data points below

            Cap (uF)   Time (microsec)
              0.33           300
              1.0            850
              1.5           1300
              2.0           1900
            */

            double ret = 0;

            if (((this._one_shot_res + this._one_shot_res_var) > 0) && (this._one_shot_cap > 0))
            {
                ret = ONE_SHOT_CAP_VOLTAGE_RANGE / (854.7 * this._one_shot_cap + 0.00001795);
            }
            else if ((this._one_shot_res + this._one_shot_res_var) > 0)
            {
                /* if no cap, the voltage changes extremely fast,
                   effectively making the one-shot time 0 */
                ret = +1e+30;
            }

            return ret;
        }


        double compute_slf_cap_charging_rate() /* in V/sec */
        {
            /* this formula was derived using the data points below

            Res (kohms)  Cap (uF)   Time (millisec)
                 47        0.47          14.3
                120        0.47          35.6
                200        0.47          59.2
                 47        1.00          28.6
                120        1.00          71.6
                200        1.00         119.0
            */
            double ret = 0;

            if (((this._slf_res + this._slf_res_var) > 0) && (this._slf_cap > 0))
            {
                ret = SLF_CAP_VOLTAGE_RANGE / (0.5885 * (this._slf_res + this._slf_res_var) * this._slf_cap + 0.001300);
            }

            return ret;
        }


        double compute_slf_cap_discharging_rate() /* in V/sec */
        {
            /* this formula was derived using the data points below

            Res (kohms)  Cap (uF)   Time (millisec)
                 47        0.47          13.32
                120        0.47          32.92
                200        0.47          54.4
                 47        1.00          26.68
                120        1.00          66.2
                200        1.00         109.6
            */
            double ret = 0;

            if (((this._slf_res + this._slf_res_var) > 0) && (this._slf_cap > 0))
            {
                ret = SLF_CAP_VOLTAGE_RANGE / (0.5413 * (this._slf_res + this._slf_res_var) * this._slf_cap + 0.001343);
            }

            return ret;
        }


        double compute_vco_cap_charging_discharging_rate() /* in V/sec */
        {
            double ret = 0;

            if (((this._vco_res + this._vco_res_var) > 0) && (this._vco_cap > 0))
            {
                ret = 0.64 * 2 * VCO_CAP_VOLTAGE_RANGE / ((this._vco_res + this._vco_res_var) * this._vco_cap);
            }

            return ret;
        }


        double compute_vco_duty_cycle() /* no measure, just a number */
        {
            double ret = 0.5;	/* 50% */

            if ((this._vco_voltage > 0) && (this._pitch_voltage != VCO_DUTY_CYCLE_50))
            {
                ret = max(0.5 * (this._pitch_voltage / this._vco_voltage), (VCO_MIN_DUTY_CYCLE / 100.0));

                ret = min(ret, 1);
            }

            return ret;
        }


        UInt32 compute_noise_gen_freq() /* in Hz */
        {
            /* this formula was derived using the data points below

             Res (ohms)   Freq (Hz)
                10k         97493
                12k         83333
                15k         68493
                22k         49164
                27k         41166
                33k         34449
                36k         31969
                47k         25126
                56k         21322
                68k         17721.5
                82k         15089.2
                100k        12712.0
                150k         8746.4
                220k         6122.4
                270k         5101.5
                330k         4217.2
                390k         3614.5
                470k         3081.7
                680k         2132.7
                820k         1801.8
                  1M         1459.9
                2.2M          705.13
                3.3M          487.59
            */

            UInt32 ret = 0;

            if (((this._noise_clock_res + this._noise_clock_res_var) >= NOISE_MIN_CLOCK_RES) &&
                ((this._noise_clock_res + this._noise_clock_res_var) <= NOISE_MAX_CLOCK_RES))
            {
                ret = (uint)(339100000 * System.Math.Pow((this._noise_clock_res + this._noise_clock_res_var), -0.8849));
            }

            return ret;
        }


        double compute_noise_filter_cap_charging_rate() /* in V/sec */
        {
            /* this formula was derived using the data points below

             R*C        Time (sec)
            .000068     .0000184
            .0001496    .0000378
            .0002244    .0000548
            .0003196    .000077
            .0015       .000248
            .0033       .000540
            .00495      .000792
            .00705      .001096
            */

            double ret = 0;

            if (((this._noise_filter_res + this._noise_filter_res_var)> 0) && (this._noise_filter_cap > 0))
            {
                ret = NOISE_CAP_VOLTAGE_RANGE / (0.1571 * (this._noise_filter_res + this._noise_filter_res_var) * this._noise_filter_cap + 0.00001430);
            }
            else if (this._noise_filter_cap > 0)
            {
                /* if no resistor, there is no current to charge the cap,
                   effectively making the filter's output constants */
                ret = +1e-30;
            }
            else if ((this._noise_filter_res + this._noise_filter_res_var) > 0)
            {
                /* if no cap, the voltage changes extremely fast,
                   effectively disabling the filter */
                ret = +1e+30;
            }

            return ret;
        }


        double compute_noise_filter_cap_discharging_rate() /* in V/sec */
        {
            /* this formula was derived using the data points below

             R*C        Time (sec)
            .000068     .000016
            .0001496    .0000322
            .0002244    .0000472
            .0003196    .0000654
            .0015       .000219
            .0033       .000468
            .00495      .000676
            .00705      .000948
            */

            double ret = 0;

            if (((this._noise_filter_res + this._noise_filter_res_var) > 0) && (this._noise_filter_cap > 0))
            {
                ret = NOISE_CAP_VOLTAGE_RANGE / (0.1331 * (this._noise_filter_res + this._noise_filter_res_var) * this._noise_filter_cap + 0.00001734);
            }
            else if (this._noise_filter_cap > 0)
            {
                /* if no resistor, there is no current to charge the cap,
                   effectively making the filter's output constants */
                ret = +1e-30;
            }
            else if ((this._noise_filter_res + this._noise_filter_res_var) > 0)
            {
                /* if no cap, the voltage changes extremely fast,
                   effectively disabling the filter */
                ret = +1e+30;
            }

            return ret;
        }


        double compute_attack_decay_cap_charging_rate()  /* in V/sec */
        {
            double ret = 0;

            if (((this._attack_res + this._attack_res_var)> 0) && (this._attack_decay_cap > 0))
            {
                ret = AD_CAP_VOLTAGE_RANGE / ((this._attack_res + this._attack_res_var) * this._attack_decay_cap);
            }
            else if (this._attack_decay_cap > 0)
            {
                /* if no resistor, there is no current to charge the cap,
                   effectively making the attack time infinite */
                ret = +1e-30;
            }
            else if ((this._attack_res + this._attack_res_var) > 0)
            {
                /* if no cap, the voltage changes extremely fast,
                   effectively making the attack time 0 */
                ret = +1e+30;
            }

            return ret;
        }


        double compute_attack_decay_cap_discharging_rate()  /* in V/sec */
        {
            double ret = 0;

            if (((this._decay_res + this._decay_res_var) > 0) && (this._attack_decay_cap > 0))
            {
                ret = AD_CAP_VOLTAGE_RANGE / ((this._decay_res + this._decay_res_var) * this._attack_decay_cap);
            }
            else if (this._attack_decay_cap > 0)
            {
                /* if no resistor, there is no current to charge the cap,
                   effectively making the decay time infinite */
                ret = +1e-30;
            }
            else if ((this._attack_res + this._attack_res_var) > 0)
            {
                /* if no cap, the voltage changes extremely fast,
                   effectively making the decay time 0 */
                ret = +1e+30;
            }

            return ret;
        }


        double compute_center_to_peak_voltage_out()
        {
            /* this formula was derived using the data points below

             Ra (kohms)  Rf (kohms)   Voltage
                150         47          1.28
                200         47          0.96
                 47         22          1.8
                100         22          0.87
                150         22          0.6
                200         22          0.45
                 47         10          0.81
                100         10          0.4
                150         10          0.27
            */

            double ret = 0;

            if (this._amplitude_res > 0)
            {
                ret = 3.818 * (this._feedback_res / this._amplitude_res) + 0.03;
            }

            return ret;
        }



        /*****************************************************************************
         *
         *  Logging functions
         *
         *****************************************************************************/

        void log_enable_line()
        {
            string[] desc = { "Enabled", "Inhibited" };

            LOG(1, String.Format("SN76477 #{0}:  Enable line (9): {1} [{2}]", this._name, this._inhibit, desc[this._inhibit]));
        }


        void log_mixer_mode()
        {
            string[] desc = {"VCO", "SLF", "Noise", "VCO/Noise","SLF/Noise", "SLF/VCO/Noise", "SLF/VCO", "Inhibit"};

            LOG(1, String.Format("SN76477 #{0}:  Mixer mode (25-27): {1} [{2}]", this._name, this._mixer_mode.ToString(), desc[this._mixer_mode]));
        }


        void log_envelope_mode()
        {
            string[] desc =	{"VCO", "One-Shot", "Mixer Only", "VCO with Alternating Polarity"};

            LOG(1, String.Format("SN76477 #{0}:  Envelope mode (1,28): {1} [{2}]", this._name, this._envelope_mode, desc[this._envelope_mode]));
        }


        void log_vco_mode()
        {
            string[] desc = {"External (Pin 16)", "Internal (SLF)"};

            LOG(1, String.Format("SN76477 #{0}:  VCO mode (22): {1} [{2}]", this._name, this._vco_mode, desc[this._vco_mode]));
        }

        public double OneShotTime
        {
            get
            {
                if (this._one_shot_cap_voltage_ext == 0)
                {
                    if (compute_one_shot_cap_charging_rate() > 0)
                    {
                        return (ONE_SHOT_CAP_VOLTAGE_RANGE * (1 / compute_one_shot_cap_charging_rate()));
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return -1;
                }
            }
        }

        void log_one_shot_time()
        {
            if (this._one_shot_cap_voltage_ext == 0)
            {
                if (compute_one_shot_cap_charging_rate() > 0)
                {
                    LOG(1, String.Format("SN76477 #{0}:  One-shot time (23,24): {1:F} sec", this._name, ONE_SHOT_CAP_VOLTAGE_RANGE * (1 / compute_one_shot_cap_charging_rate())));
                }
                else
                {
                    LOG(1, String.Format("SN76477 #{0}:  One-shot time (23,24): N/A", this._name));
                }
            }
            else
            {
                LOG(1, String.Format("SN76477 #{0}:  One-shot time (23,24): External (cap = {1:F}V)", this._name, this._one_shot_cap_voltage));
            }
        }

        public double SLF_Frequency
        {
            get
            {
                if (this._slf_cap_voltage_ext == 0)
                {
                    if (compute_slf_cap_charging_rate() > 0)
                    {
                        double charging_time = (1 / compute_slf_cap_charging_rate()) * SLF_CAP_VOLTAGE_RANGE;
                        double discharging_time = (1 / compute_slf_cap_discharging_rate()) * SLF_CAP_VOLTAGE_RANGE;

                        return (1 / (charging_time + discharging_time));
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return 0;
                }
            }
        }

        void log_slf_freq()
        {
            if (this._slf_cap_voltage_ext == 0)
            {
                if (compute_slf_cap_charging_rate() > 0)
                {
                    double charging_time = (1 / compute_slf_cap_charging_rate()) * SLF_CAP_VOLTAGE_RANGE;
                    double discharging_time = (1 / compute_slf_cap_discharging_rate()) * SLF_CAP_VOLTAGE_RANGE;

                    LOG(1, String.Format("SN76477 #{0}:  SLF frequency (20,21): {1:F} Hz", this._name, 1 / (charging_time + discharging_time)));
                }
                else
                {
                    LOG(1, String.Format("SN76477 #{0}:  SLF frequency (20,21): N/A", this._name));
                }
            }
            else
            {
                LOG(1, String.Format("SN76477 #{0}:  SLF frequency (20,21): External (cap = {1:F}V)", this._name, this._slf_cap_voltage));
            }
        }


        void log_vco_pitch_voltage()
        {
            LOG(1, String.Format("SN76477 #{0}:  VCO pitch voltage (19): {1:F}V", this._name, this._pitch_voltage));
        }

        public double VCO_DutyCycle
        {
            get
            {
                return compute_vco_duty_cycle() * 100.0;
            }
        }


        void log_vco_duty_cycle()
        {
            LOG(1, String.Format("SN76477 #{0}:  VCO duty cycle (16,19): {1:F}%", this._name, compute_vco_duty_cycle() * 100.0));
        }

        public double VCO_FrequencyMax
        {
            get
            {
                if (this._vco_cap_voltage_ext == 0)
                {
                    if (compute_vco_cap_charging_discharging_rate() > 0)
                    {
                        double max_freq = compute_vco_cap_charging_discharging_rate() / (2 * VCO_TO_SLF_VOLTAGE_DIFF);

                        return max_freq;
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return -1;
                }
            }
        }

        public double VCO_FrequencyMin
        {
            get
            {
                if (this._vco_cap_voltage_ext == 0)
                {
                    if (compute_vco_cap_charging_discharging_rate() > 0)
                    {
                        double min_freq = compute_vco_cap_charging_discharging_rate() / (2 * VCO_CAP_VOLTAGE_RANGE);
                        
                        return min_freq;
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return -1;
                }
            }
        }

        void log_vco_freq()
        {
            if (this._vco_cap_voltage_ext == 0)
            {
                if (compute_vco_cap_charging_discharging_rate() > 0)
                {
                    double min_freq = compute_vco_cap_charging_discharging_rate() / (2 * VCO_CAP_VOLTAGE_RANGE);
                    double max_freq = compute_vco_cap_charging_discharging_rate() / (2 * VCO_TO_SLF_VOLTAGE_DIFF);

                    LOG(1, String.Format("SN76477 #{0}:  VCO frequency (17,18): {1:F} Hz - {2:F} Hz", this._name, min_freq, max_freq));
                }
                else
                {
                    LOG(1, String.Format("SN76477 #{0}:  VCO frequency (17,18): N/A", this._name));
                }
            }
            else
            {
                LOG(1, String.Format("SN76477 #{0}:  VCO frequency (17,18): External (cap = {1:F}V)", this._name, this._vco_cap_voltage));
            }
        }

        public double VCO_ExternalVoltage_Frequency
        {
            get
            {
                if (this._vco_voltage <= VCO_MAX_EXT_VOLTAGE)
                {
                    double min_freq = compute_vco_cap_charging_discharging_rate() / (2 * VCO_CAP_VOLTAGE_RANGE);
                    double max_freq = compute_vco_cap_charging_discharging_rate() / (2 * VCO_TO_SLF_VOLTAGE_DIFF);

                    return (min_freq + ((max_freq - min_freq) * this._vco_voltage / VCO_MAX_EXT_VOLTAGE));
                }
                else
                {
                    return 0;
                }
            }
        }

        void log_vco_ext_voltage()
        {
            if (this._vco_voltage <= VCO_MAX_EXT_VOLTAGE)
            {
                double min_freq = compute_vco_cap_charging_discharging_rate() / (2 * VCO_CAP_VOLTAGE_RANGE);
                double max_freq = compute_vco_cap_charging_discharging_rate() / (2 * VCO_TO_SLF_VOLTAGE_DIFF);

                LOG(1, String.Format("SN76477 #{0}:  VCO ext. voltage (16): {1:F}V ({2:F} Hz)", this._name,
                        this._vco_voltage,
                        min_freq + ((max_freq - min_freq) * this._vco_voltage / VCO_MAX_EXT_VOLTAGE)));
            }
            else
            {
                LOG(1, String.Format("SN76477 #{0}:  VCO ext. voltage (16): {1:F}V (saturated, no output)", this._name, this._vco_voltage));
            }
        }

        public double NoiseGenerator_Frequency
        {
            get
            {
                if (this._noise_clock_ext != 0)
                {
                    return -1;
                }
                else
                {
                    if (compute_noise_gen_freq() > 0)
                    {
                        return compute_noise_gen_freq();
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
        }

        void log_noise_gen_freq()
        {
            if (this._noise_clock_ext != 0)
            {
                LOG(1, String.Format("SN76477 #{0}:  Noise gen frequency (4): External", this._name));
            }
            else
            {
                if (compute_noise_gen_freq() > 0)
                {
                    LOG(1, String.Format("SN76477 #{0}:  Noise gen frequency (4): {1} Hz", this._name, compute_noise_gen_freq()));
                }
                else
                {
                    LOG(1, String.Format("SN76477 #{0}:  Noise gen frequency (4): N/A", this._name));
                }
            }
        }

        public double NoiseFilter_Frequency
        {
            get
            {
                if (this._noise_filter_cap_voltage_ext == 0)
                {
                    double charging_rate = compute_noise_filter_cap_charging_rate();

                    if (charging_rate > 0)
                    {
                        if (charging_rate < 1000000.0)
                        {
                            double charging_time = (1 / charging_rate) * NOISE_CAP_VOLTAGE_RANGE;
                            double discharging_time = (1 / charging_rate) * NOISE_CAP_VOLTAGE_RANGE;

                            return (1 / (charging_time + discharging_time));
                        }
                        else
                        {
                            return 0;
                        }
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return -1;
                }
            }
        }


        void log_noise_filter_freq()
        {
            if (this._noise_filter_cap_voltage_ext == 0)
            {
                double charging_rate = compute_noise_filter_cap_charging_rate();

                if (charging_rate > 0)
                {
                    if (charging_rate < 1000000.0)
                    {
                        double charging_time = (1 / charging_rate) * NOISE_CAP_VOLTAGE_RANGE;
                        double discharging_time = (1 / charging_rate) * NOISE_CAP_VOLTAGE_RANGE;

                        LOG(1, String.Format("SN76477 #{0}:  Noise filter frequency (5,6): {1:F} Hz", this._name, 1 / (charging_time + discharging_time)));
                    }
                    else
                    {
                        LOG(1, String.Format("SN76477 #{0}:  Noise filter frequency (5,6): Very Large (Filtering Disabled)", this._name));
                    }
                }
                else
                {
                    LOG(1, String.Format("SN76477 #{0}:  Noise filter frequency (5,6): N/A", this._name));
                }
            }
            else
            {
                LOG(1, String.Format("SN76477 #{0}:  Noise filter frequency (5,6): External (cap = {1:F}V)", this._name, this._noise_filter_cap));
            }
        }


        public double Attack_Time
        {
            get
            {
                if (this._attack_decay_cap_voltage_ext == 0)
                {
                    if (compute_attack_decay_cap_charging_rate() > 0)
                    {
                        return (AD_CAP_VOLTAGE_RANGE * (1 / compute_attack_decay_cap_charging_rate()));
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return -1;
                }
            }
        }

        void log_attack_time()
        {
            if (this._attack_decay_cap_voltage_ext == 0)
            {
                if (compute_attack_decay_cap_charging_rate() > 0)
                {
                    LOG(1, String.Format("SN76477 #{0}:  Attack time (8,10): {1:F} sec", this._name, AD_CAP_VOLTAGE_RANGE * (1 / compute_attack_decay_cap_charging_rate())));
                }
                else
                {
                    LOG(1, String.Format("SN76477 #{0}:  Attack time (8,10): N/A", this._name));
                }
            }
            else
            {
                LOG(1, String.Format("SN76477 #{0}:  Attack time (8,10): External (cap = {1}V)", this._name, this._attack_decay_cap_voltage));
            }
        }

        public double Decay_Time
        {
            get
            {
                if (this._attack_decay_cap_voltage_ext == 0)
                {
                    if (compute_attack_decay_cap_discharging_rate() > 0)
                    {
                        return (AD_CAP_VOLTAGE_RANGE * (1 / compute_attack_decay_cap_discharging_rate()));
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return -1;
                }
            }
        }

        void log_decay_time()
        {
            if (this._attack_decay_cap_voltage_ext == 0)
            {
                if (compute_attack_decay_cap_discharging_rate() > 0)
                {
                    LOG(1, String.Format("SN76477 #{0}:  Decay time (7,8): {1:F} sec", this._name, AD_CAP_VOLTAGE_RANGE * (1 / compute_attack_decay_cap_discharging_rate())));
                }
                else
                {
                    LOG(1, String.Format("SN76477 #{0}:  Decay time (8,10): N/A", this._name));
                }
            }
            else
            {
                LOG(1, String.Format("SN76477 #{0}:  Decay time (7, 8): External (cap = {1}V)", this._name, this._attack_decay_cap_voltage));
            }
        }


        void log_voltage_out()
        {
            LOG(1, String.Format("SN76477 #{0}:  Voltage OUT range (11,12): {1}V - {2}V (clips above {3}V)",
                    this._name,
                    OUT_CENTER_LEVEL_VOLTAGE + compute_center_to_peak_voltage_out() * out_neg_gain[(int)(AD_CAP_VOLTAGE_MAX * 10)],
                    OUT_CENTER_LEVEL_VOLTAGE + compute_center_to_peak_voltage_out() * out_pos_gain[(int)(AD_CAP_VOLTAGE_MAX * 10)],
                    OUT_HIGH_CLIP_THRESHOLD));
        }

        public double OUTVOLTAGEMIN
        {
            get
            {
                return OUT_CENTER_LEVEL_VOLTAGE + compute_center_to_peak_voltage_out() * out_neg_gain[(int)(AD_CAP_VOLTAGE_MAX * 10)];
            }
        }

        public double OUTVOLTAGEMAX
        {
            get
            {
                return OUT_CENTER_LEVEL_VOLTAGE + compute_center_to_peak_voltage_out() * out_pos_gain[(int)(AD_CAP_VOLTAGE_MAX * 10)];
            }
        }

        void log_complete_state()
        {
            log_enable_line();
            log_mixer_mode();
            log_envelope_mode();
            log_vco_mode();
            log_one_shot_time();
            log_slf_freq();
            log_vco_freq();
            log_vco_ext_voltage();
            log_vco_pitch_voltage();
            log_vco_duty_cycle();
            log_noise_filter_freq();
            log_noise_gen_freq();
            log_attack_time();
            log_decay_time();
            log_voltage_out();
        }



        /*****************************************************************************
         *
         *  .WAV file functions
         *
         *****************************************************************************/

        void open_wav_file(uint length, string filename)
        {
            try
            {

                file = new BinaryWriter(File.Open(filename, FileMode.Create));
            }
            catch
            {


                if (file == null)
                {
                    LOG(1, String.Format("SN76477 #{0}:  Error opening file: {1}", this._name, filename));
                    return;
                }
            }

            LOG(1, String.Format("SN76477 #{0}:  Logging output: {1}", this._name, filename));

            UInt16 NumChannels = 1;
            UInt32 SampleRate = 44100;
            UInt16 AudioFormat = 1;
            UInt16 BlockAlign = (UInt16)(NumChannels * ((UInt16)2));
            UInt16 BitsPerSample = 16;

            file.Write('R');
            file.Write('I');
            file.Write('F');
            file.Write('F');

            UInt32 TotalSize = length * NumChannels * 2 + 36;
            file.Write(TotalSize);

            file.Write('W');
            file.Write('A');
            file.Write('V');
            file.Write('E');

            file.Write('f');
            file.Write('m');
            file.Write('t');
            file.Write(' ');

            UInt32 SubChunk1Size = 16;
            file.Write(SubChunk1Size);

            file.Write(AudioFormat);

            file.Write(NumChannels);

            file.Write(SampleRate);

            UInt32 ByteRate = SampleRate * NumChannels * 2;
            file.Write(ByteRate);

            file.Write(BlockAlign);

            file.Write(BitsPerSample);

            file.Write('d');
            file.Write('a');
            file.Write('t');
            file.Write('a');

            UInt32 SubChunk2Size = length * NumChannels * 2;
            file.Write(SubChunk2Size);
        }


        void close_wav_file()
        {
            try
            {
                file.Close();
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("Error closing WAV");
            }
        }


        void add_wav_data(Int16 data)
        {
            try
            {
                if (file != null)
                {
                    file.Write(data);
                }
            }
            catch
            {
            }
        }


        /*****************************************************************************
         *
         *  Noise generator
         *
         *****************************************************************************/

        void intialize_noise()
        {
            this._rng = 0;
        }


        UInt32 generate_next_real_noise_bit()
        {
            UInt32 out1 = ((this._rng >> 28) & 1) ^ ((this._rng >> 0) & 1);

            /* if bits 0-4 and 28 are all zero then force the output to 1 */
            if ((this._rng & 0x1000001f) == 0)
            {
                out1 = 1;
            }

            this._rng = (this._rng >> 1) | (out1 << 30);

            return out1;
        }



        /*****************************************************************************
         *
         *  Set enable input
         *
         *****************************************************************************/
        public uint INHIBIT
        {
            get
            {
                return this._inhibit;
            }
            set
            {
                if (value != this._inhibit)
                {
                    this._inhibit = value;

                    /* if falling edge */
                    if (this._inhibit == 0)
                    {
                        /* start the attack phase */
                        this._attack_decay_cap_voltage = AD_CAP_VOLTAGE_MIN;

                        /* one-shot runs regardless of envelope mode */
                        this._one_shot_running_ff = 1;
                    }

                    log_enable_line();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set mixer select inputs
         *
         *****************************************************************************/
        public MixerMode MixerMode
        {
            get
            {
                return (MixerMode)this._mixer_mode;
            }
            set
            {
                if (((int)value) != this._mixer_mode)
                {
                    this._mixer_mode = ((uint)value);
                    log_mixer_mode();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set envelope select inputs
         *
         *****************************************************************************/
        public EnvelopeMode EnvelopeMode
        {
            get
            {
                return (EnvelopeMode)this._envelope_mode;
            }
            set
            {
                if ((UInt32)value != (this._envelope_mode))
                {
                    this._envelope_mode = (UInt32)value;
                    log_envelope_mode();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set VCO select input
         *
         *****************************************************************************/
        public uint VCOSELECT
        {
            get
            {
                return this._vco_mode;
            }
            set
            {
                if (value != this._vco_mode)
                {
                    this._vco_mode = value;
                    log_vco_mode();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set one-shot resistor
         *
         *****************************************************************************/
        public double ONESHOTRES
        {
            get
            {
                return this._one_shot_res;
            }
            set
            {
                if (value != this._one_shot_res)
                {
                    this._one_shot_res = value;
                    log_one_shot_time();
                }
            }
        }

        public double ONESHOTRESVAR
        {
            get
            {
                return this._one_shot_res_var;
            }
            set
            {
                if (value != this._one_shot_res_var)
                {
                    this._one_shot_res_var = value;
                    log_one_shot_time();
                }
            }
        }

        public double ONESHOTRESVARMAX
        {
            get
            {
                return this._one_shot_res_var_max;
            }
            set
            {
                if (value != this._one_shot_res_var_max)
                {
                    this._one_shot_res_var_max = value;
                    log_one_shot_time();
                }
            }
        }
 
        /*****************************************************************************
        *
        *  Set one-shot capacitor
        *
        *****************************************************************************/
        public double ONESHOTCAP
        {
            get
            {
                return this._one_shot_cap;
            }
            set
            {
                if (value != this._one_shot_cap)
                {
                    this._one_shot_cap = value;
                    log_one_shot_time();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set the voltage on the one-shot capacitor
         *
         *****************************************************************************/
        public bool ONESHOTCAPVOLTAGEEXTERNAL
        {
            get
            {
                return (_one_shot_cap_voltage_ext == 1);
            }
            set
            {
                if (value)
                {
                    if (_one_shot_cap_voltage_ext == 0)
                    {
                        _one_shot_cap_voltage_ext = 1;
                        log_one_shot_time();
                    }
                }
                else
                {
                    if (_one_shot_cap_voltage_ext == 1)
                    {
                        _one_shot_cap_voltage_ext = 0;
                        log_one_shot_time();
                    }
                }
            }
        }

        public double ONESHOTCAPVOLTAGE
        {
            get
            {
                return this._one_shot_cap_voltage;
            }
            set
            {
                if ((this._one_shot_cap_voltage_ext == 1) && (value != this._one_shot_cap_voltage))
                {
                    this._one_shot_cap_voltage = value;
                    log_one_shot_time();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set SLF resistor
         *
         *****************************************************************************/
        public double SLFRES
        {
            get
            {
                return this._slf_res;
            }
            set
            {
                if (value != this._slf_res)
                {
                    this._slf_res = value;
                    log_slf_freq();
                }
            }
        }

        public double SLFRESVAR
        {
            get
            {
                return this._slf_res_var;
            }
            set
            {
                if (value != this._slf_res_var)
                {
                    this._slf_res_var = value;
                    log_slf_freq();
                }
            }
        }

        public double SLFRESVARMAX
        {
            get
            {
                return this._slf_res_var_max;
            }
            set
            {
                if (value != this._slf_res_var_max)
                {
                    this._slf_res_var_max = value;
                    log_slf_freq();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set SLF capacitor
         *
         *****************************************************************************/
        public double SLFCAP
        {
            get
            {
                return _slf_cap;
            }
            set
            {
                if (value != this._slf_cap)
                {
                    this._slf_cap = value;

                    log_slf_freq();
                }
            }
        }


        /*****************************************************************************
         *
         *  Set the voltage on the SLF capacitor
         *
         *  This is an alternate way of controlling the VCO as described in the book
         *
         *****************************************************************************/
        public bool SLFCAPVOLTAGEEXTERNAL
        {
            get
            {
                return (_slf_cap_voltage_ext == 1);
            }
            set
            {
                if (value)
                {
                    if (_slf_cap_voltage_ext == 0)
                    {
                        _slf_cap_voltage_ext = 1;
                        log_slf_freq();
                    }
                }
                else
                {
                    if (_slf_cap_voltage_ext == 1)
                    {
                        _slf_cap_voltage_ext = 0;
                        log_slf_freq();
                    }
                }
            }
        }

        public double SLFCAPVOLTAGE
        {
            get
            {
                return this._slf_cap_voltage;
            }
            set
            {
                if ((this._slf_cap_voltage_ext == 1) && (value != this._slf_cap_voltage))
                {
                    this._slf_cap_voltage = value;
                    log_slf_freq();
                }
            }
        }



        /*****************************************************************************
         *
         *  Set VCO resistor
         *
         *****************************************************************************/
        public double VCORES
        {
            get
            {
                return this._vco_res;
            }
            set
            {
                if (value != this._vco_res)
                {
                    this._vco_res = value;
                    log_vco_freq();
                }
            }
        }

        public double VCORESVAR
        {
            get
            {
                return this._vco_res_var;
            }
            set
            {
                if (value != this._vco_res_var)
                {
                    this._vco_res_var = value;
                    log_vco_freq();
                }
            }
        }

        public double VCORESVARMAX
        {
            get
            {
                return this._vco_res_var_max;
            }
            set
            {
                if (value != this._vco_res_var_max)
                {
                    this._vco_res_var_max = value;
                    log_vco_freq();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set VCO capacitor
         *
         *****************************************************************************/
        public double VCOCAP
        {
            get
            {
                return this._vco_cap;
            }
            set
            {
                if (value != this._vco_cap)
                {
                    this._vco_cap = value;
                    log_vco_freq();
                }
            }

        }


        /*****************************************************************************
         *
         *  Set the voltage on the VCO capacitor
         *
         *****************************************************************************/
        public bool VCOCAPVOLTAGEEXTERNAL
        {
            get
            {
                return (_vco_cap_voltage_ext == 1);
            }
            set
            {
                if (value)
                {
                    if (_vco_cap_voltage_ext == 0)
                    {
                        _vco_cap_voltage_ext = 1;
                        log_vco_freq();
                    }
                }
                else
                {
                    if (_vco_cap_voltage_ext == 1)
                    {
                        _vco_cap_voltage_ext = 0;
                        log_vco_freq();
                    }
                }
            }
        }

        public double VCOCAPVOLTAGE
        {
            get
            {
                return this._vco_cap_voltage;
            }
            set
            {
                if ((this._vco_cap_voltage_ext == 1) && (value != this._vco_cap_voltage))
                {
                    this._vco_cap_voltage = value;
                    log_vco_freq();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set VCO voltage
         *
         *****************************************************************************/
        public double VCOEXTVOLTAGE
        {
            get
            {
                return this._vco_voltage;
            }
            set
            {
                if (value != this._vco_voltage)
                {
                    this._vco_voltage = value;
                    log_vco_ext_voltage();
                    log_vco_duty_cycle();
                }
            }

        }

        /*****************************************************************************
         *
         *  Set pitch voltage
         *
         *****************************************************************************/
        public double VCOPITCHVOLTAGE
        {
            get
            {
                return this._pitch_voltage;
            }
            set
            {
                if (value != this._pitch_voltage)
                {
                    this._pitch_voltage = value;
                    log_vco_pitch_voltage();
                    log_vco_duty_cycle();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set noise external clock
         *
         *****************************************************************************/
        public uint NOISECLOCK
        {
            get
            {
                return this._noise_clock;
            }
            set
            {
                if (value != this._noise_clock)
                {
                    this._noise_clock = value;

                    /* on the rising edge shift generate next value,
                       if external control is enabled */
                    if ((this._noise_clock != 0) && (this._noise_clock_ext != 0))
                    {
                        this._real_noise_bit_ff = generate_next_real_noise_bit();
                    }
                }
            }
        }

        /*****************************************************************************
         *
         *  Set noise clock resistor
         *
         *****************************************************************************/
        public double NOISECLOCKRES
        {
            get
            {
                return _noise_clock_res;
            }
            set
            {
                if (((value == 0) && (this._noise_clock_ext == 0)) ||
                ((value != 0) && (value != this._noise_clock_res)))
                {
                    if (value == 0)
                    {
                        this._noise_clock_ext = 1;
                    }
                    else
                    {
                        this._noise_clock_ext = 0;

                        this._noise_clock_res = value;
                    }

                    log_noise_gen_freq();
                }
            }
        }

        public double NOISECLOCKRESVAR
        {
            get
            {
                return this._noise_clock_res_var;
            }
            set
            {
                if (value != this._noise_clock_res_var)
                {
                    this._noise_clock_res_var = value;
                    log_noise_gen_freq();
                }
            }
        }

        public double NOISECLOCKRESVARMAX
        {
            get
            {
                return this._noise_clock_res_var_max;
            }
            set
            {
                if (value != this._noise_clock_res_var_max)
                {
                    this._noise_clock_res_var_max = value;
                    log_noise_gen_freq();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set noise filter resistor
         *
         *****************************************************************************/
        public double NOISEFILTERRES
        {
            get 
            { 
                return this._noise_filter_res; 
            }
            set
            {
                if (value != this._noise_filter_res)
                {
                    this._noise_filter_res = value;
                    log_noise_filter_freq();
                }
            }
        }

        public double NOISEFILTERRESVAR
        {
            get
            {
                return this._noise_filter_res_var;
            }
            set
            {
                if (value != this._noise_filter_res_var)
                {
                    this._noise_filter_res_var = value;
                    log_noise_filter_freq();
                }
            }
        }

        public double NOISEFILTERRESVARMAX
        {
            get
            {
                return this._noise_filter_res_var_max;
            }
            set
            {
                if (value != this._noise_filter_res_var_max)
                {
                    this._noise_filter_res_var_max = value;
                    log_noise_filter_freq();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set noise filter capacitor
         *
         *****************************************************************************/
        public double NOISEFILTERCAP
        {
            get
            {
                return this._noise_filter_cap;
            }
            set
            {
                if (value != this._noise_filter_cap)
                {
                    this._noise_filter_cap = value;
                    log_noise_filter_freq();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set the voltage on the noise filter capacitor
         *
         *****************************************************************************/
        public bool NOISEFILTERCAPVOLTAGEEXTERNAL
        {
            get
            {
                return (_noise_filter_cap_voltage_ext == 1);
            }
            set
            {
                if (value)
                {
                    if (_noise_filter_cap_voltage_ext == 0)
                    {
                        _noise_filter_cap_voltage_ext = 1;
                        log_noise_filter_freq();
                    }
                }
                else
                {
                    if (_noise_filter_cap_voltage_ext == 1)
                    {
                        _noise_filter_cap_voltage_ext = 0;
                        log_noise_filter_freq();
                    }
                }
            }
        }

        public double NOISEFILTERCAPVOLTAGE
        {
            get
            {
                return this._noise_filter_cap_voltage;
            }
            set
            {
                if ((this._noise_filter_cap_voltage_ext == 1) && (value != this._noise_filter_cap_voltage))
                {
                    this._noise_filter_cap_voltage = value;
                    log_noise_filter_freq();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set attack resistor
         *
         *****************************************************************************/
        public double ENVATKRES
        {
            get
            {
                return this._attack_res;
            }
            set
            {
                if (value != this._attack_res)
                {
                    this._attack_res = value;
                    log_attack_time();
                }
            }
        }

        public double ENVATKRESVAR
        {
            get
            {
                return this._attack_res_var;
            }
            set
            {
                if (value != this._attack_res_var)
                {
                    this._attack_res_var = value;
                    log_attack_time();
                }
            }
        }

        public double ENVATKRESVARMAX
        {
            get
            {
                return this._attack_res_var_max;
            }
            set
            {
                if (value != this._attack_res_var_max)
                {
                    this._attack_res_var_max = value;
                    log_attack_time();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set decay resistor
         *
         *****************************************************************************/
        public double ENVDECRES
        {
            get
            {
                return this._decay_res;
            }
            set
            {
                if (value != this._decay_res)
                {
                    this._decay_res = value;
                    log_decay_time();
                }
            }
        }

        public double ENVDECRESVAR
        {
            get
            {
                return this._decay_res_var;
            }
            set
            {
                if (value != this._decay_res_var)
                {
                    this._decay_res_var = value;
                    log_decay_time();
                }
            }
        }

        public double ENVDECRESVARMAX
        {
            get
            {
                return this._decay_res_var_max;
            }
            set
            {
                if (value != this._decay_res_var_max)
                {
                    this._decay_res_var_max = value;
                    log_decay_time();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set attack/decay capacitor
         *
         *****************************************************************************/
        public double ENVCAP
        {
            get
            {
                return this._attack_decay_cap;
            }
            set
            {
                if (value != this._attack_decay_cap)
                {
                    this._attack_decay_cap = value;
                    log_attack_time();
                    log_decay_time();
                }
            }
        }

        /*****************************************************************************
         *
         *  Set the voltage on the attack/decay capacitor
         *
         *****************************************************************************/
        public bool ENVCAPVOLTAGEEXTERNAL
        {
            get
            {
                return (_attack_decay_cap_voltage_ext == 1);
            }
            set
            {
                if (value)
                {
                    if (_attack_decay_cap_voltage_ext == 0)
                    {
                        _attack_decay_cap_voltage_ext = 1;
                        log_attack_time();
                        log_decay_time();
                    }
                }
                else
                {
                    if (_attack_decay_cap_voltage_ext == 1)
                    {
                        _attack_decay_cap_voltage_ext = 0;
                        log_attack_time();
                        log_decay_time();
                    }
                }
            }
        }

        public double ENVCAPVOLTAGE
        {
            get
            {
                return this._attack_decay_cap_voltage_ext;
            }
            set
            {
                if ((this._attack_decay_cap_voltage_ext == 1) && (value != this._attack_decay_cap_voltage))
                {
                    this._attack_decay_cap_voltage = value;
                    log_attack_time();
                    log_decay_time();
                }
            }
        }



        /*****************************************************************************
         *
         *  Set amplitude resistor
         *
         *****************************************************************************/
        public double AMPLITUDERES
        {
            get
            {
                return this._amplitude_res;
            }
            set
            {
                if (value != this._amplitude_res)
                {
                    this._amplitude_res = value;
                    log_voltage_out();
                }

            }
        }

        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
            }

        }

        /*****************************************************************************
         *
         *  Set feedback resistor
         *
         *****************************************************************************/
        public double FEEDBACKRES
        {
            get
            {
                return this._feedback_res;
            }
            set
            {
                if (value != this._feedback_res)
                {
                    this._feedback_res = value;
                    log_voltage_out();
                }

            }
        }

        /*****************************************************************************
         *
         *  Sample generation
         *
         *****************************************************************************/
        public Int16[] GenerateSamples(uint length, string filename)
        {
            double one_shot_cap_charging_step;
            double one_shot_cap_discharging_step;
            double slf_cap_charging_step;
            double slf_cap_discharging_step;
            double vco_duty_cycle_multiplier;
            double vco_cap_charging_step;
            double vco_cap_discharging_step;
            double vco_cap_voltage_max;
            UInt32 noise_gen_freq;
            double noise_filter_cap_charging_step;
            double noise_filter_cap_discharging_step;
            double attack_decay_cap_charging_step;
            double attack_decay_cap_discharging_step;
            int attack_decay_cap_charging;
            double voltage_out;
            double center_to_peak_voltage_out;

            Int16[] buffer = new Int16[length];
            UInt32 bufferpos = 0;

            if (filename != "") open_wav_file(length, filename);

            /* compute charging values, doing it here ensures that we always use the latest values */
            one_shot_cap_charging_step = compute_one_shot_cap_charging_rate() / this.sample_rate;
            one_shot_cap_discharging_step = compute_one_shot_cap_discharging_rate() / this.sample_rate;

            slf_cap_charging_step = compute_slf_cap_charging_rate() / this.sample_rate;
            slf_cap_discharging_step = compute_slf_cap_discharging_rate() / this.sample_rate;

            vco_duty_cycle_multiplier = (1 - compute_vco_duty_cycle()) * 2;
            vco_cap_charging_step = compute_vco_cap_charging_discharging_rate() / vco_duty_cycle_multiplier / this.sample_rate;
            vco_cap_discharging_step = compute_vco_cap_charging_discharging_rate() * vco_duty_cycle_multiplier / this.sample_rate;

            noise_filter_cap_charging_step = compute_noise_filter_cap_charging_rate() / this.sample_rate;
            noise_filter_cap_discharging_step = compute_noise_filter_cap_discharging_rate() / this.sample_rate;
            noise_gen_freq = compute_noise_gen_freq();

            attack_decay_cap_charging_step = compute_attack_decay_cap_charging_rate() / this.sample_rate;
            attack_decay_cap_discharging_step = compute_attack_decay_cap_discharging_rate() / this.sample_rate;

            center_to_peak_voltage_out = compute_center_to_peak_voltage_out();

            /* process 'length' number of samples */
            while ( (length--) != 0)
            {
                /* update the one-shot cap voltage */
                if (this._one_shot_cap_voltage_ext == 0)
                {
                    if (this._one_shot_running_ff != 0)
                    {
                        /* charging */
                        this._one_shot_cap_voltage = min(this._one_shot_cap_voltage + one_shot_cap_charging_step, ONE_SHOT_CAP_VOLTAGE_MAX);
                    }
                    else
                    {
                        /* discharging */
                        this._one_shot_cap_voltage = max(this._one_shot_cap_voltage - one_shot_cap_discharging_step, ONE_SHOT_CAP_VOLTAGE_MIN);
                    }
                }

                if (this._one_shot_cap_voltage >= ONE_SHOT_CAP_VOLTAGE_MAX)
                {
                    this._one_shot_running_ff = 0;
                }


                /* update the SLF (super low frequency oscillator) */
                if (this._slf_cap_voltage_ext == 0)
                {
                    /* internal */
                    if (this._slf_out_ff == 0)
                    {
                        /* charging */
                        this._slf_cap_voltage = min(this._slf_cap_voltage + slf_cap_charging_step, SLF_CAP_VOLTAGE_MAX);
                    }
                    else
                    {
                        /* discharging */
                        this._slf_cap_voltage = max(this._slf_cap_voltage - slf_cap_discharging_step, SLF_CAP_VOLTAGE_MIN);
                    }
                }

                if (this._slf_cap_voltage >= SLF_CAP_VOLTAGE_MAX)
                {
                    this._slf_out_ff = 1;
                }
                else if (this._slf_cap_voltage <= SLF_CAP_VOLTAGE_MIN)
                {
                    this._slf_out_ff = 0;
                }


                /* update the VCO (voltage controlled oscillator) */
                if (this._vco_mode != 0)
                {
                    /* VCO is controlled by SLF */
                    vco_cap_voltage_max = this._slf_cap_voltage + VCO_TO_SLF_VOLTAGE_DIFF;
                }
                else
                {
                    /* VCO is controlled by external voltage */
                    vco_cap_voltage_max = this._vco_voltage + VCO_TO_SLF_VOLTAGE_DIFF;
                }

                if (this._vco_cap_voltage_ext == 0)
                {
                    if (this._vco_out_ff == 0)
                    {
                        /* charging */
                        this._vco_cap_voltage = min(this._vco_cap_voltage + vco_cap_charging_step, vco_cap_voltage_max);
                    }
                    else
                    {
                        /* discharging */
                        this._vco_cap_voltage = max(this._vco_cap_voltage - vco_cap_discharging_step, VCO_CAP_VOLTAGE_MIN);
                    }
                }

                if (this._vco_cap_voltage >= vco_cap_voltage_max)
                {
                    if (this._vco_out_ff == 0)
                    {
                        /* positive edge */
                        if (this._vco_alt_pos_edge_ff != 0) this._vco_alt_pos_edge_ff = 0;
                        else this._vco_alt_pos_edge_ff = 1;
                    }

                    this._vco_out_ff = 1;
                }
                else if (this._vco_cap_voltage <= VCO_CAP_VOLTAGE_MIN)
                {
                    this._vco_out_ff = 0;
                }


                /* update the noise generator */
                while ((this._noise_clock_ext == 0) && (this._noise_gen_count <= noise_gen_freq))
                {
                    this._noise_gen_count = this._noise_gen_count + this.sample_rate;

                    this._real_noise_bit_ff = generate_next_real_noise_bit();
                }

                this._noise_gen_count = this._noise_gen_count - noise_gen_freq;


                /* update the noise filter */
                if (this._noise_filter_cap_voltage_ext == 0)
                {
                    /* internal */
                    if (this._real_noise_bit_ff != 0)
                    {
                        /* charging */
                        this._noise_filter_cap_voltage = min(this._noise_filter_cap_voltage + noise_filter_cap_charging_step, NOISE_CAP_VOLTAGE_MAX);
                    }
                    else
                    {
                        /* discharging */
                        this._noise_filter_cap_voltage = max(this._noise_filter_cap_voltage - noise_filter_cap_discharging_step, NOISE_CAP_VOLTAGE_MIN);
                    }
                }

                /* check the thresholds */
                if (this._noise_filter_cap_voltage >= NOISE_CAP_HIGH_THRESHOLD)
                {
                    this._filtered_noise_bit_ff = 0;
                }
                else if (this._noise_filter_cap_voltage <= NOISE_CAP_LOW_THRESHOLD)
                {
                    this._filtered_noise_bit_ff = 1;
                }


                /* based on the envelope mode figure out the attack/decay phase we are in */
                switch (this._envelope_mode)
                {
                    case 0:		/* VCO */
                        attack_decay_cap_charging = (int)this._vco_out_ff;
                        break;

                    case 1:		/* one-shot */
                        attack_decay_cap_charging = (int)this._one_shot_running_ff;
                        break;

                    case 2:
                    default:	/* mixer only */
                        attack_decay_cap_charging = 1;	/* never a decay phase */
                        break;

                    case 3:		/* VCO with alternating polarity */
                        if ((this._vco_out_ff != 0) && (this._vco_alt_pos_edge_ff != 0))
                            attack_decay_cap_charging = 1;
                        else
                            attack_decay_cap_charging = 0;
                        break;
                }


                /* update a/d cap voltage */
                if (this._attack_decay_cap_voltage_ext == 0)
                {
                    if (attack_decay_cap_charging != 0)
                    {
                        if (attack_decay_cap_charging_step > 0)
                        {
                            this._attack_decay_cap_voltage = min(this._attack_decay_cap_voltage + attack_decay_cap_charging_step, AD_CAP_VOLTAGE_MAX);
                        }
                        else
                        {
                            /* no attack, voltage to max instantly */
                            this._attack_decay_cap_voltage = AD_CAP_VOLTAGE_MAX;
                        }
                    }
                    else
                    {
                        /* discharging */
                        if (attack_decay_cap_discharging_step > 0)
                        {
                            this._attack_decay_cap_voltage = max(this._attack_decay_cap_voltage - attack_decay_cap_discharging_step, AD_CAP_VOLTAGE_MIN);
                        }
                        else
                        {
                            /* no decay, voltage to min instantly */
                            this._attack_decay_cap_voltage = AD_CAP_VOLTAGE_MIN;
                        }
                    }
                }


                /* mix the output, if enabled, or not saturated by the VCO */
                if ((this._inhibit == 0) && (this._vco_cap_voltage <= VCO_CAP_VOLTAGE_MAX))
                {
                    UInt32 out1;

                    /* enabled */
                    switch (this._mixer_mode)
                    {
                        case 0:		/* VCO */
                            out1 = this._vco_out_ff;
                            break;

                        case 1:		/* SLF */
                            out1 = this._slf_out_ff;
                            break;

                        case 2:		/* noise */
                            out1 = this._filtered_noise_bit_ff;
                            break;

                        case 3:		/* VCO and noise */
                            out1 = this._vco_out_ff & this._filtered_noise_bit_ff;
                            break;

                        case 4:		/* SLF and noise */
                            out1 = this._slf_out_ff & this._filtered_noise_bit_ff;
                            break;

                        case 5:		/* VCO, SLF and noise */
                            out1 = this._vco_out_ff & this._slf_out_ff & this._filtered_noise_bit_ff;
                            break;

                        case 6:		/* VCO and SLF */
                            out1 = this._vco_out_ff & this._slf_out_ff;
                            break;

                        case 7:		/* inhibit */
                        default:
                            out1 = 0;
                            break;
                    }

                    /* determine the OUT voltage from the attack/delay cap voltage and clip it */
                    if (out1 != 0)
                    {
                        voltage_out = OUT_CENTER_LEVEL_VOLTAGE + center_to_peak_voltage_out * out_pos_gain[(int)(this._attack_decay_cap_voltage * 10)];
                        voltage_out = min(voltage_out, OUT_HIGH_CLIP_THRESHOLD);
                    }
                    else
                    {
                        voltage_out = OUT_CENTER_LEVEL_VOLTAGE + center_to_peak_voltage_out * out_neg_gain[(int)(this._attack_decay_cap_voltage * 10)];
                        voltage_out = max(voltage_out, OUT_LOW_CLIP_THRESHOLD);
                    }
                }
                else
                {
                    /* disabled */
                    voltage_out = OUT_CENTER_LEVEL_VOLTAGE;
                }


                /* convert it to a signed 16-bit sample,
                   -32767 = OUT_LOW_CLIP_THRESHOLD
                        0 = OUT_CENTER_LEVEL_VOLTAGE
                    32767 = 2 * OUT_CENTER_LEVEL_VOLTAGE + OUT_LOW_CLIP_THRESHOLD

                              / Vout - Vmin    \
                    sample = |  ----------- - 1 | * 32767
                              \ Vcen - Vmin    /
                 */

                double sample = ((((voltage_out - OUT_LOW_CLIP_THRESHOLD) / (OUT_CENTER_LEVEL_VOLTAGE - OUT_LOW_CLIP_THRESHOLD)) - 1) * 32767);

                buffer[bufferpos] = (Int16)sample;
                bufferpos++;

                if (filename != "") add_wav_data((Int16)sample);

            }

            if (filename != "") close_wav_file();

            return buffer;
        }

        public void Update()
        {
        }


        /*****************************************************************************
         *
         *  Constructor
         *
         *****************************************************************************/
        public SN76477()
        {
            this._name = "SN76477";

            this.sample_rate = 44100;

            this.intialize_noise();

            this.MixerMode = MixerMode.VCO;
            this.EnvelopeMode = EnvelopeMode.VCO;

            this.SLFRES = 0;
            this.SLFRESVAR = 0;
            this.SLFRESVARMAX = 1000000;
            this.SLFCAP = 0;
            this.SLFCAPVOLTAGE = EXTERNAL_VOLTAGE_DISCONNECT;
            this.VCOSELECT = 1;
            this.VCORES = 0;
            this.VCORESVAR = 0;
            this.VCORESVARMAX = 1000000;
            this.VCOCAP = 0;
            this.VCOCAPVOLTAGEEXTERNAL = false;
            this.VCOCAPVOLTAGE = EXTERNAL_VOLTAGE_DISCONNECT;
            this.VCOEXTVOLTAGE = EXTERNAL_VOLTAGE_DISCONNECT;
            this.VCOPITCHVOLTAGE = 5;
            this.NOISECLOCK = 0;
            this.NOISECLOCKRES = 0;
            this.NOISECLOCKRESVAR = 0;
            this.NOISECLOCKRESVARMAX = 1000000;
            this.NOISEFILTERRES = 0;
            this.NOISEFILTERRESVAR = 0;
            this.NOISEFILTERRESVARMAX = 1000000;
            this.NOISEFILTERCAP = 0;
            this.NOISEFILTERCAPVOLTAGE = EXTERNAL_VOLTAGE_DISCONNECT;
            this.ONESHOTRES = 0;
            this.ONESHOTRESVAR = 0;
            this.ONESHOTRESVARMAX = 1000000;
            this.ONESHOTCAP = 0;
            this.ONESHOTCAPVOLTAGE = EXTERNAL_VOLTAGE_DISCONNECT;
            this.INHIBIT = 0;
            this.ENVATKRES = 0;
            this.ENVATKRESVAR = 0;
            this.ENVATKRESVARMAX = 1000000;
            this.ENVDECRES = 0;
            this.ENVDECRESVAR = 0;
            this.ENVDECRESVARMAX = 1000000;
            this.ENVCAP = 0;
            this.ENVCAPVOLTAGE = EXTERNAL_VOLTAGE_DISCONNECT;
            this.AMPLITUDERES = 100000;
            this.FEEDBACKRES = 22000;

            this._one_shot_cap_voltage = ONE_SHOT_CAP_VOLTAGE_MIN;
            this._slf_cap_voltage = SLF_CAP_VOLTAGE_MIN;
            this._vco_cap_voltage = VCO_CAP_VOLTAGE_MIN;
            this._noise_filter_cap_voltage = NOISE_CAP_VOLTAGE_MIN;
            this._attack_decay_cap_voltage = AD_CAP_VOLTAGE_MIN;

            log_complete_state();
        }

    }
}
