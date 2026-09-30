using Modbus.Device;
using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace WpfTest.Services
{
    /// <summary>
    /// Modbus RTU 串口通信服务（NModbus4 封装）
    /// </summary>
    public class ModbusService : IDisposable
    {
        private SerialPort serialPort;
        private IModbusSerialMaster? modbusService;
        private bool isDisposed;

        /// <summary>
        /// 构造函数，初始化串口配置
        /// </summary>
        /// <param name="portName">串口号，如 COM3</param>
        /// <param name="baudRate">波特率，如 9600</param>
        public ModbusService(string portName, int baudRate)
        {
            Console.WriteLine("正在初始化Modbus...");
            serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
            //设置读写超时时间
            serialPort.ReadTimeout = 1000;
            serialPort.WriteTimeout = 1000;
        }

        /// <summary>
        /// 串口是否已打开且 Modbus 主站已创建
        /// </summary>
        public bool IsConnected => serialPort != null && serialPort.IsOpen && modbusService != null;

        /// <summary>
        /// 打开连接（幂等：已连接时直接返回）
        /// 注意：此方法只打开串口和创建主站对象，不验证从站是否在线。
        /// 验证从站在线请调用 TestConnection。
        /// </summary>
        public void Connect()
        {
            if (!serialPort.IsOpen)
            {
                serialPort.Open();
            }
            if (modbusService == null)
            {
                modbusService = ModbusSerialMaster.CreateRtu(serialPort);
            }
        }

        /// <summary>
        /// 测试从站是否在线：读取1个保持寄存器，有响应返回 true，超时/异常返回 false。
        /// 必须在 Connect() 之后调用。
        /// </summary>
        public bool TestConnection(byte slaveAddress)
        {
            if (modbusService == null) return false;
            try
            {
                // 读地址0的1个寄存器，验证从站是否响应
                ushort[] result = modbusService.ReadHoldingRegisters(slaveAddress, 0, 1);
                return result != null && result.Length >= 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 异步读取保持型寄存器（Holding Registers）
        /// </summary>
        /// <param name="slaveAddress">从站地址</param>
        /// <param name="startAddress">起始寄存器地址</param>
        /// <param name="numRegisters">寄存器数量</param>
        public async Task<ushort[]?> ReadHoldingRegistersAsync(byte slaveAddress, ushort startAddress, ushort numRegisters)
        {
            if (modbusService == null)
            {
                Console.WriteLine("Modbus未连接");
                return null;
            }
            try
            {
                return await modbusService.ReadHoldingRegistersAsync(slaveAddress, startAddress, numRegisters);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取寄存器出错:{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 资源释放
        /// </summary>
        public void Dispose()
        {
            if (!isDisposed)
            {
                if (serialPort != null && serialPort.IsOpen)
                {
                    serialPort.Close();
                    serialPort.Dispose();
                }
                modbusService = null;
                isDisposed = true;
            }
        }
    }
}
