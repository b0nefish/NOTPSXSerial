using System;
using System.IO.Ports;

using System.Net.Sockets;
using System.Net;
using System.Threading;


public enum GPR{
    
    stat,
    badv,       // repurposed for unirom

    // GPR
    r0, at, v0, v1, a0, a1, a2, a3,
    t0, t1, t2, t3, t4, t5, t6, t7,
    s0, s1, s2, s3, s4, s5, s6, s7,
    t8, t9, k0, k1, gp, sp, fp, ra,

    rapc,
    hi, lo,
    sr,
    caus,
    // GPR

    unknown0,    unknown1,
    unknown2,    unknown3,
    unknown4,    unknown5,
    unknown6,    unknown7,
    unknown9,

    COUNT // C# only

}

// The PSX's Thread Control Block (usually TCB[0])
public class TCB{
       
    public UInt32[] regs = new UInt32[ (int)GPR.COUNT ];

}


public enum PSXState{ normal, halted };

public class GDB{

    public static bool enabled = false;

    public static SerialPort serial => Program.activeSerial;
    public static TCB tcb = new TCB();
    public static PSXState psxState = PSXState.normal;

	public const int TCB_LENGTH_BYTES = (int)GPR.COUNT * 4;

	public static Socket socket;

    public static void Init(){
		
		Console.WriteLine( "Checking if Unirom is in debug mode..." );

		// if it returns true, we might enter /m (monitor) mode, etc
		if (
			!TransferLogic.ChallengeResponse( CommandMode.DEBUG )
		) {
			Console.WriteLine( "Couldn't determine if Unirom is in debug mode." );
			return;
		}

		Console.WriteLine( "Grabbing initial state..." );

		TransferLogic.Command_DumpRegs();

		Console.WriteLine( "Monitoring sio..." );

		InitSocket();

		MonitorSIOAndSocket();

    }

	private static void InitSocket(){
		
		IPHostEntry ipHostInfo = Dns.GetHostEntry( Dns.GetHostName() );
		Console.WriteLine( "Info: " + ipHostInfo + " " + ipHostInfo.Aliases[0] + " " + ipHostInfo.AddressList[0] );

		IPAddress ip = ipHostInfo.AddressList[0];

		IPEndPoint localEndpoint = new IPEndPoint( ip, 3333 );

		socket = new Socket( AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp );

		socket.Bind( localEndpoint );

		socket.Listen( 2 );

	}

	public static bool GetRegs(){
		
		// read the pointer to TCB[0]
		byte[] ptrBuffer = new byte[4];
		if ( !TransferLogic.Command_DumpBytes( 0x80000110, 4, ptrBuffer ) ){
			return false;
		}

		UInt32 tcbPtr = BitConverter.ToUInt32( ptrBuffer, 0 );

		Console.WriteLine( "TCB PTR " + tcbPtr.ToString("X") );

		byte[] tcbBytes = new byte[TCB_LENGTH_BYTES];
		if ( !TransferLogic.Command_DumpBytes( tcbPtr, (int)GPR.COUNT *4, tcbBytes ) ){
			return false;
		}
		
		Buffer.BlockCopy( tcbBytes, 0, tcb.regs, 0, tcbBytes.Length );
				
		return true;

	}

	public static bool SetRegs(){
		
		// read the pointer to TCB[0]
		byte[] ptrBuffer = new byte[4];
		if ( !TransferLogic.Command_DumpBytes( 0x80000110, 4, ptrBuffer ) ){
			return false;
		}

		UInt32 tcbPtr = BitConverter.ToUInt32( ptrBuffer, 0 );
		Console.WriteLine( "TCB PTR " + tcbPtr.ToString("X") );

		// Convert regs back to a byte array and bang them back out
		byte[] tcbBytes = new byte[TCB_LENGTH_BYTES];
		Buffer.BlockCopy( tcb.regs, 0, tcbBytes, 0, TCB_LENGTH_BYTES );

		TransferLogic.Command_SendBin( tcbPtr, tcbBytes, Program.CalculateChecksum( tcbBytes ) );
				
		return true;

	}

	public static void DumpRegs(){
		int tab = 0;
		for( int i = 0; i < (int)GPR.COUNT -9; i++ ){			
			Console.Write( "\t {0} =0x{1}", ((GPR)i).ToString().PadLeft(4), tcb.regs[ i ].ToString("X8") );
			// this format won't change, so there's no issue hardcoding them
			if ( tab++%4 == 3 || i == 1 || i == 33 || i == 34 ){
				Console.WriteLine();
				tab = 0;
			}
		}
		Console.WriteLine();

		UInt32 cause = tcb.regs[ (int)GPR.caus ] >> 2;
		
		switch ( cause ) {
			case 0x04:
				Console.WriteLine("AdEL - Data Load or instr fetch (0x{0})\n", cause);
				break;
			case 0x05:
				Console.WriteLine("AdES - Data Store (unaligned?) (0x{0})\n", cause);
				break;
			case 0x06:
				Console.WriteLine("IBE - Bus Error on instr fetch (0x{0})\n", cause);
				break;
			case 0x07:
				Console.WriteLine("DBE - Bus Error on data load/store (0x{0})\n", cause);
				break;
			case 0x08:
				Console.WriteLine("SYS - Unconditional Syscall (0x{0})\n", cause);
				break;
			case 0x09:
				Console.WriteLine("BP - Break! (0x{0})\n", cause);
				break;
			case 0x0A:
				Console.WriteLine("RI - Reserved Instruction (0x{0})\n", cause);
				break;
			case 0x0B:
				Console.WriteLine("CpU - Coprocessor unavailable (0x{0})\n", cause);
				break;
			case 0x0C:
				Console.WriteLine("Ov - Arithmetic overflow (0x{0})\n", cause);
				break;

			default:
				Console.WriteLine("Code {0}!\n", cause);
				break;
		}
		

	}

    public static void MonitorSIOAndSocket(){
        
        string responeBuffer = ":)";

        while( true ){

            if ( serial.BytesToRead != 0 ){
            
                responeBuffer += (char)serial.ReadByte();

                if ( responeBuffer.Length > 4 ){
                    responeBuffer = responeBuffer.Remove( 0, 1 );
                }

                Console.WriteLine( "\r ResponseBuffer: " + responeBuffer + "  Len = " + responeBuffer.Length );

                // PSX telling us it was halted.
                if ( responeBuffer == "HLTD" ){
					responeBuffer = "";
                    Console.WriteLine( "PSX was halted" );
                }

            }

        }

    }


}
