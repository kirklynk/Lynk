using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.I2c;
using GHIElectronics.TinyCLR.Devices.Spi;
using System;
using System.Collections;
using System.Text;
using System.Threading;

namespace Lynk.TinyCLR.Adafruit.MiniTFTFeatherWing
{
    public enum ST7735CommandId : byte
    {
        //System
        NOP = 0x00,
        SWRESET = 0x01,
        RDDID = 0x04,
        RDDST = 0x09,
        RDDPM = 0x0A,
        RDDMADCTL = 0x0B,
        RDDCOLMOD = 0x0C,
        RDDIM = 0x0D,
        RDDSM = 0x0E,
        SLPIN = 0x10,
        SLPOUT = 0x11,
        PTLON = 0x12,
        NORON = 0x13,
        INVOFF = 0x20,
        INVON = 0x21,
        GAMSET = 0x26,
        DISPOFF = 0x28,
        DISPON = 0x29,
        CASET = 0x2A,
        RASET = 0x2B,
        RAMWR = 0x2C,
        RAMRD = 0x2E,
        PTLAR = 0x30,
        TEOFF = 0x34,
        TEON = 0x35,
        MADCTL = 0x36,
        IDMOFF = 0x38,
        IDMON = 0x39,
        COLMOD = 0x3A,
        RDID1 = 0xDA,
        RDID2 = 0xDB,
        RDID3 = 0xDC,

        //Panel
        FRMCTR1 = 0xB1,
        FRMCTR2 = 0xB2,
        FRMCTR3 = 0xB3,
        INVCTR = 0xB4,
        DISSET5 = 0xB6,
        PWCTR1 = 0xC0,
        PWCTR2 = 0xC1,
        PWCTR3 = 0xC2,
        PWCTR4 = 0xC3,
        PWCTR5 = 0xC4,
        VMCTR1 = 0xC5,
        VMOFCTR = 0xC7,
        WRID2 = 0xD1,
        WRID3 = 0xD2,
        NVCTR1 = 0xD9,
        NVCTR2 = 0xDE,
        NVCTR3 = 0xDF,
        GAMCTRP1 = 0xE0,
        GAMCTRN1 = 0xE1,
    }
    public enum Buttons
    {

        BUTTON_UP_PIN = 1 << 2,
        BUTTON_LEFT_PIN = 1 << 3,
        BUTTON_DOWN_PIN = 1 << 4,

        BUTTON_RIGHT_PIN = 1 << 7,
        BUTTON_B_PIN = 1 << 9,
        BUTTON_A_PIN = 1 << 10,
        BUTTON_SELECT_PIN = 1 << 11,
        ALL = (1 << 2) | (1 << 3) | (1 << 4) | (1 << 7) | (1 << 9) | (1 << 10) | (1 << 11)
    }
    public enum ScreenRotation
    {
        Inverted = 1,
        NonInverted = 3
    }
    //Used for raising button events
    public delegate void ButtonStateChangedEventHandler(Buttons button, bool newState);
    public class MiniTFTController
    {
        private readonly byte[] buffer1 = new byte[1];
        private readonly byte[] buffer4 = new byte[4];

        private readonly SpiDevice spi;
        private readonly I2cDevice _i2cDevice;
        private readonly GpioPin control;


        const byte ST77XX_MADCTL_MY = 0x80;
        const byte ST77XX_MADCTL_MX = 0x40;
        const byte ST77XX_MADCTL_MV = 0x20;
        const byte ST77XX_MADCTL_RGB = 0x00;


        const byte SEESAW_STATUS_BASE = 0x00;
        const byte SEESAW_STATUS_SWRST = 0x7F;
        const byte SEESAW_GPIO_BASE = 0x01;
        const byte SEESAW_TIMER_BASE = 0x08;
        /* 
         * GPIO module function addres registers
         */
        const byte SEESAW_GPIO_DIRSET_BULK = 0x02;
        const byte SEESAW_GPIO_DIRCLR_BULK = 0x03;
        const byte SEESAW_GPIO_BULK = 0x04;
        const byte SEESAW_GPIO_BULK_SET = 0x05;
        const byte SEESAW_GPIO_BULK_CLR = 0x06;
        const byte SEESAW_GPIO_PULLENSET = 0x0B;
        /*
         * status module function addres registers
         */
        const byte SEESAW_STATUS_HW_ID = 0x01;
        const byte SEESAW_TIMER_PWM = 0x01;
        const byte SEESAW_HW_ID_CODE = 0x55;
        int _colstart = 24;
        int _rowstart = 0;

        int MaxWidth = 80;
        int MaxHeight = 160;
        public int Height => MaxWidth;
        public int Width => MaxHeight;

        public double BacklightIntensity { get; private set; }
        public ScreenRotation Rotation { get; private set; }

        //Seesaw reset pin 
        const int _resetPin = 1 << 8;
        int _lastPressed = 0;
        private int _xstart;
        private int _ystart;
        private int _width;
        private int _height;

        public event ButtonStateChangedEventHandler OnButtonStateChanged;

        public MiniTFTController(I2cController controller, SpiController spiController, GpioPin chipSelect, GpioPin control, int debounceMs = 50)
        {
            spi = spiController.GetDevice(new SpiConnectionSettings
            {

                Mode = SpiMode.Mode3,
                ClockFrequency = 12_000_000,
                ChipSelectType = SpiChipSelectType.Gpio,
                ChipSelectLine = chipSelect
            });
            _i2cDevice = controller.GetDevice(new I2cConnectionSettings(0x5E));
            this.control = control;
            this.control.SetDriveMode(GpioPinDriveMode.Output);

            Initialize();


            //Thread used to read the seesaw pin values and raise event as needed
            new Thread(() =>
            {
                var buttons = (int)(Buttons.BUTTON_UP_PIN | Buttons.BUTTON_DOWN_PIN | Buttons.BUTTON_LEFT_PIN | Buttons.BUTTON_RIGHT_PIN | Buttons.BUTTON_SELECT_PIN | Buttons.BUTTON_A_PIN | Buttons.BUTTON_B_PIN);
                if (debounceMs < 20)
                    debounceMs = 20;
                while (true)
                {

                    var buffer = new byte[8];
                    _i2cDevice.Write(new byte[] { SEESAW_GPIO_BASE, SEESAW_GPIO_BULK });
                    _i2cDevice.Read(buffer);

                    int ret = (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];
                    var active = (int)buttons - (ret & buttons);
                    if (active != _lastPressed)
                    {
                        var isPressed = active > _lastPressed;

                        var diff = isPressed ? active - _lastPressed : _lastPressed - active;

                        if (diff >= (int)Buttons.BUTTON_SELECT_PIN)
                        {
                            OnButtonStateChanged?.Invoke(Buttons.BUTTON_SELECT_PIN, isPressed);
                            diff -= (int)Buttons.BUTTON_SELECT_PIN;
                        }
                        if (diff >= (int)Buttons.BUTTON_A_PIN)
                        {
                            OnButtonStateChanged?.Invoke(Buttons.BUTTON_A_PIN, isPressed);
                            diff -= (int)Buttons.BUTTON_A_PIN;
                        }
                        if (diff >= (int)Buttons.BUTTON_B_PIN)
                        {
                            OnButtonStateChanged?.Invoke(Buttons.BUTTON_B_PIN, isPressed);
                            diff -= (int)Buttons.BUTTON_B_PIN;
                        }
                        if (diff >= (int)Buttons.BUTTON_RIGHT_PIN)
                        {
                            OnButtonStateChanged?.Invoke(Buttons.BUTTON_RIGHT_PIN, isPressed);
                            diff -= (int)Buttons.BUTTON_RIGHT_PIN;
                        }
                        if (diff >= (int)Buttons.BUTTON_DOWN_PIN)
                        {
                            OnButtonStateChanged?.Invoke(Buttons.BUTTON_DOWN_PIN, isPressed);
                            diff -= (int)Buttons.BUTTON_DOWN_PIN;
                        }
                        if (diff >= (int)Buttons.BUTTON_LEFT_PIN)
                        {
                            OnButtonStateChanged?.Invoke(Buttons.BUTTON_LEFT_PIN, isPressed);
                            diff -= (int)Buttons.BUTTON_LEFT_PIN;
                        }
                        if (diff >= (int)Buttons.BUTTON_UP_PIN)
                        {
                            OnButtonStateChanged?.Invoke(Buttons.BUTTON_UP_PIN, isPressed);
                            diff -= (int)Buttons.BUTTON_UP_PIN;
                        }


                    }
                    _lastPressed = active;

                    Thread.Sleep(debounceMs);
                }

            }).Start();
        }



        private void Initialize()
        {
            #region Seesaw initialization


            _i2cDevice.Write(new byte[] { SEESAW_STATUS_BASE, SEESAW_STATUS_SWRST, 0xFF });
            Thread.Sleep(1000);
            var buffer = new byte[1];
            _i2cDevice.Write(new byte[] { SEESAW_STATUS_BASE, SEESAW_STATUS_HW_ID });
            _i2cDevice.Read(buffer);

            if (buffer[0] != SEESAW_HW_ID_CODE)
            {
                throw new Exception("Did not find device");
            }

            var buttons = Buttons.BUTTON_UP_PIN | Buttons.BUTTON_DOWN_PIN | Buttons.BUTTON_LEFT_PIN | Buttons.BUTTON_RIGHT_PIN | Buttons.BUTTON_SELECT_PIN | Buttons.BUTTON_A_PIN | Buttons.BUTTON_B_PIN;

            WritePinMode((int)buttons, 3);
            OnButtonStateChanged?.Invoke(Buttons.ALL, false);
            Thread.Sleep(100);
            WritePinMode(_resetPin, 1);
            #endregion

            #region ST7735 initialization

            //Reset ST7735
            DigitalWrite(_resetPin, true);
            Thread.Sleep(50);
            DigitalWrite(_resetPin, false);
            Thread.Sleep(50);
            DigitalWrite(_resetPin, true);



            SendCommand(ST7735CommandId.SWRESET);
            Thread.Sleep(120);

            SendCommand(ST7735CommandId.SLPOUT);
            Thread.Sleep(120);

            SendCommand(ST7735CommandId.FRMCTR1);
            SendData(new byte[] { 0x01, 0x2C, 0x2D });


            SendCommand(ST7735CommandId.FRMCTR2);
            SendData(new byte[] { 0x01, 0x2C, 0x2D });

            SendCommand(ST7735CommandId.FRMCTR3);
            SendData(new byte[] { 0x01, 0x2C, 0x2D, 0x01, 0x2C, 0x2D });

            SendCommand(ST7735CommandId.INVCTR);
            SendData(new byte[] { 0x07 });

            SendCommand(ST7735CommandId.PWCTR1);
            SendData(new byte[] { 0xA2, 0x02, 0x84 });

            SendCommand(ST7735CommandId.PWCTR2);
            SendData(new byte[] { 0xC5 });

            SendCommand(ST7735CommandId.PWCTR3);
            SendData(new byte[] { 0x0A, 0x00 });

            SendCommand(ST7735CommandId.PWCTR4);
            SendData(new byte[] { 0x8A, 0x2A });

            SendCommand(ST7735CommandId.PWCTR5);
            SendData(new byte[] { 0x8A, 0xEE });

            SendCommand(ST7735CommandId.COLMOD);
            SendData(new byte[] { 0x05 });

            SendCommand(ST7735CommandId.VMCTR1);
            SendData(new byte[] { 0x0E });

            SendCommand(ST7735CommandId.GAMCTRP1);
            this.SendData(new byte[] { 0x02, 0x1c, 0x07, 0x12, 0x37, 0x32, 0x29, 0x2d, 0x29, 0x25, 0x2B, 0x39, 0x00, 0x01, 0x03, 0x10 });

            SendCommand(ST7735CommandId.GAMCTRN1);
            this.SendData(new byte[] { 0x03, 0x1d, 0x07, 0x06, 0x2E, 0x2C, 0x29, 0x2D, 0x2E, 0x2E, 0x37, 0x3F, 0x00, 0x00, 0x02, 0x10 });

            SendCommand(ST7735CommandId.NORON);
            Thread.Sleep(10);

            SendCommand(ST7735CommandId.MADCTL);
            SendData(new byte[] { 0xC0 });

            SetRotation(ScreenRotation.NonInverted);

            SetDrawWindow(0, 0, _width, _height);

            SetBacklightIntensityPercentage(100);
            #endregion

        }

        public void Dispose()
        {
            spi.Dispose();
            control.Dispose();

        }

        public void Enable() => SendCommand(ST7735CommandId.DISPON);
        public void Disable() => SendCommand(ST7735CommandId.DISPOFF);

        private void SendCommand(ST7735CommandId command)
        {
            buffer1[0] = (byte)command;
            control.Write(GpioPinValue.Low);
            spi.Write(buffer1);
        }


        private void SendData(byte[] data)
        {
            control.Write(GpioPinValue.High);
            spi.Write(data);
        }

        public void SetRotation(ScreenRotation rotate)
        {


            Rotation = rotate;

            byte madctl = 0x00;
            switch (rotate)
            {

                case ScreenRotation.Inverted:
                    madctl = ST77XX_MADCTL_MY | ST77XX_MADCTL_MV | ST77XX_MADCTL_RGB;
                    _width = MaxHeight;
                    _height = MaxWidth;
                    _ystart = _colstart;
                    _xstart = _rowstart;
                    break;

                case ScreenRotation.NonInverted:
                    madctl = ST77XX_MADCTL_MX | ST77XX_MADCTL_MV | ST77XX_MADCTL_RGB;
                    _width = MaxHeight;
                    _height = MaxWidth;
                    _ystart = _colstart;
                    _xstart = _rowstart;
                    break;

            }

            SendCommand(ST7735CommandId.MADCTL);
            SendData(new byte[] { madctl });

        }

        private void SetDrawWindow(int x, int y, int width, int height)
        {
            y += _ystart;
            x += _xstart;

            buffer4[1] = (byte)x;
            buffer4[3] = (byte)(x + width - 1);
            SendCommand(ST7735CommandId.CASET);
            SendData(buffer4);

            buffer4[1] = (byte)y;
            buffer4[3] = (byte)(y + height - 1);
            SendCommand(ST7735CommandId.RASET);
            SendData(buffer4);
        }

        private void SendDrawCommand()
        {
            SendCommand(ST7735CommandId.RAMWR);
            control.Write(GpioPinValue.High);
        }

        public void DrawBuffer(byte[] buffer)
        {
            SendDrawCommand();

            BitConverter.SwapEndianness(buffer, 2);

            spi.Write(buffer);
            BitConverter.SwapEndianness(buffer, 2);
        }

        public void DrawBuffer(byte[] buffer, int x, int y, int width, int height)
        {
            SetDrawWindow(x, y, width, height);

            DrawBuffer(buffer, x, y, width, height, MaxWidth, 1, 1);
        }

        private void DrawBuffer(byte[] buffer, int x, int y, int width, int height, int originalWidth, int columnMultiplier, int rowMultiplier)
        {

            SendDrawCommand();

            BitConverter.SwapEndianness(buffer, 2);

            spi.Write(buffer, x, y, width, height, originalWidth, columnMultiplier, rowMultiplier);

            BitConverter.SwapEndianness(buffer, 2);
        }


        #region Seesaw functions
        private void WritePinMode(int pins, byte mode)
        {
            var data = new byte[] { (byte)(pins >> 24), (byte)(pins >> 16), (byte)(pins >> 8), (byte)pins };
            var cmd = new byte[6];
            Array.Copy(data, 0, cmd, 2, 4);
            switch (mode)
            {
                case 1: //OUTPUT
                    cmd[0] = SEESAW_GPIO_BASE;
                    cmd[1] = SEESAW_GPIO_DIRSET_BULK;
                    _i2cDevice.Write(cmd);
                    break;
                case 2: //INPUT
                    cmd[0] = SEESAW_GPIO_BASE;
                    cmd[1] = SEESAW_GPIO_DIRCLR_BULK;
                    _i2cDevice.Write(cmd);
                    break;
                case 3: //INPUT PULLUP
                    cmd[0] = SEESAW_GPIO_BASE;
                    cmd[1] = SEESAW_GPIO_DIRCLR_BULK;
                    _i2cDevice.Write(cmd);

                    cmd[1] = SEESAW_GPIO_PULLENSET;
                    _i2cDevice.Write(cmd);

                    cmd[1] = SEESAW_GPIO_BULK_SET;
                    _i2cDevice.Write(cmd);
                    break;
                case 4: //INPUT PULLDOWN
                    cmd[0] = SEESAW_GPIO_BASE;
                    cmd[1] = SEESAW_GPIO_DIRCLR_BULK;
                    _i2cDevice.Write(cmd);

                    cmd[1] = SEESAW_GPIO_PULLENSET;
                    _i2cDevice.Write(cmd);

                    cmd[1] = SEESAW_GPIO_BULK_CLR;
                    _i2cDevice.Write(cmd);
                    break;
                default:
                    break;
            }

        }
        public void SetBacklightIntensityPercentage(double val)
        {
            if (val > 100)
                val = 100;

            if (val < 0)
                val = 0;

            BacklightIntensity = val;

            var intensity = (ulong)(0xFFFF - (val / 100 * 0xFFFF));

            _i2cDevice.Write(new byte[] { SEESAW_TIMER_BASE, SEESAW_TIMER_PWM, 0x0, (byte)(intensity >> 8), (byte)intensity });
        }

        void DigitalWrite(int pins, bool value)
        {
            var data = new byte[] { (byte)(pins >> 24), (byte)(pins >> 16), (byte)(pins >> 8), (byte)pins };
            var cmd = new byte[6];
            Array.Copy(data, 0, cmd, 2, 4);
            cmd[0] = SEESAW_GPIO_BASE;
            if (value)
                cmd[1] = SEESAW_GPIO_BULK_SET;
            else
                cmd[1] = SEESAW_GPIO_BULK_CLR;

            _i2cDevice.Write(cmd);
        }
        #endregion
    }
}
