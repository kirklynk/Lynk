using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.I2c;
using GHIElectronics.TinyCLR.Devices.Spi;
using GHIElectronics.TinyCLR.Pins;
using Lynk.TinyCLR.Encoder;
using System;
using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace TinyCLRSample
{
    class Program
    {
        static void Main()
        {

            var i2cController = I2cController.FromName(GHIElectronics.TinyCLR.Pins.SC20100.I2cBus.I2c1);
            var spi4Controller = SpiController.FromName(SC20100.SpiBus.Spi4);
            var gpioController = GpioController.GetDefault();
            var encoder = new RotaryEncoderController(gpioController.OpenPin(SC20260.GpioPin.PC11), gpioController.OpenPin(SC20260.GpioPin.PC12));
            encoder.OnValueChanged += (direction, type, val) => {
                Debug.WriteLine($"{direction} {type} {val}");
            };
            encoder.Enable();
            Thread.Sleep(-1);

        }
    }
}
