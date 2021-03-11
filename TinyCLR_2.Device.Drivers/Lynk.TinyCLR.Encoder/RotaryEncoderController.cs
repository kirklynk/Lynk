using GHIElectronics.TinyCLR.Devices.Gpio;
using System;
using System.Collections;
using System.Text;
using System.Threading;

namespace Lynk.TinyCLR.Encoder
{
    public enum RotationDirection  { CW, CCW }
    public enum ValueChangeType { Continuous, Limit }

    public delegate void EncoderValueChangeHandler(RotationDirection direction, ValueChangeType type, float value);
    public class RotaryEncoderController
    {
        private readonly GpioPin _dt;
        private readonly GpioPin _clk;
        private readonly GpioPin _sw;
        private readonly Thread _worker;
        private float _count = 0;
        public event EncoderValueChangeHandler OnValueChanged;
        public bool Enabled { get; private set; } = false;
        public ValueChangeType ValueChangeType { get;  set; }

        public RotaryEncoderController(GpioPin dtPin, GpioPin clkPin, GpioPin sw = null, ValueChangeType type = ValueChangeType.Limit, float min = 0, float max = 100 )
        {
            var time = TimeSpan.FromMilliseconds(1);
            dtPin.SetDriveMode(GpioPinDriveMode.InputPullUp);
            dtPin.DebounceTimeout = time;

            ValueChangeType = type;
            clkPin.SetDriveMode(GpioPinDriveMode.InputPullUp);
            clkPin.DebounceTimeout = time;

            _dt = dtPin;
            _clk = clkPin;
            _sw = sw;
            _count = min;

            _worker = new Thread(() =>
             {
                 var lastClk = _clk.Read();
                 Enabled = true;
                 while (Enabled)
                 {
                     var current = _clk.Read();
                     var d = _dt.Read();
                     if (current != lastClk)
                     {
                         if (current == GpioPinValue.High)
                         {
                             var rotation = RotationDirection.CW;
                             if (current == d)
                             {
                                 rotation = RotationDirection.CCW;
                                 _count--;
                                
                             }
                             else
                             {
                                 _count++;
                                 rotation = RotationDirection.CW;
                                 //OnValueChanged?.Invoke(RotationDirection.CW, ValueChangeType, _count);
                             }
                             if (ValueChangeType == ValueChangeType.Limit) {
                                 if (_count < min)
                                     _count = min;
                                 if (_count > max)
                                     _count = max;
                             }
                             OnValueChanged?.Invoke(rotation, ValueChangeType, _count);
                         }
                     }
                     lastClk = current;
                     
                 }
             });
        }

        public void Enable()
        {
            if (!Enabled)
            {
                _worker.Start();
            }
        }

        public void Disable()
        {
            if (Enabled)
            {
                Enabled = false;
            }
        }
    }
}
