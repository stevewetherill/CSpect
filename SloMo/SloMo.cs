// ********************************************************************************************************************************************
//      CSpect SloMo extension, allowing dumping of screen writes in the CSpect emulator
//      Written by:
//                  Steve Wetherill
//      contributions by:
//                  
//      Released under the GNU 3 license - please see license file for more details
//
//      This extension uses the EXE extension method and traps trying to execute an instruction at RST $08,
//      and the Read/Write on IO ports for file streaming
//
// ********************************************************************************************************************************************
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Plugin;

namespace SloMo
{

    // **********************************************************************
    /// <summary>
    ///     SloMo screen capture
    /// </summary>
    // **********************************************************************
    public class Slow : iPlugin
    {
        #region Plugin interface

        public const int SCREEN_BITMAP_LENGTH = 6144;
        public const int SCREEN_ATTRIBUTE_LENGTH = 768;

        public const int SCREEN_BITMAP_BASE = 0x4000;
        public const int SCREEN_ATTRIBUTE_BASE = SCREEN_BITMAP_BASE + SCREEN_BITMAP_LENGTH;

        public iCSpect CSpect;

        public byte[] ScreenBytes;

        public int Frame;

        private const int SCREENCAPMODE_NONE = 0;
        private const int SCREENCAPMODE_BYTES = 1;
        private const int SCREENCAPMODE_BUFFER = 2;
        private const int SCREENCAPMODE_SIDEWIZE_TRIGGER = 3;
        private const int CHEATS_SIDEWIZE = 4;

        private int ScreenCapMode = SCREENCAPMODE_NONE;
        
        // **********************************************************************
        /// <summary>
        ///     Init the SloMo Plugin
        /// </summary>
        /// <returns>
        ///     List of addresses we're monitoring
        /// </returns>
        // **********************************************************************
        public List<sIO> Init(iCSpect _CSpect)
        {
            Console.WriteLine("SloMo plugin added");

            CSpect = _CSpect;

            Frame = 0;
            ScreenBytes = new byte[SCREEN_BITMAP_LENGTH + SCREEN_ATTRIBUTE_LENGTH];
            
            for (int i = SCREEN_BITMAP_LENGTH; i < SCREEN_BITMAP_LENGTH + SCREEN_ATTRIBUTE_LENGTH; i++)
            {
                ScreenBytes[i] = 0x47;
            }

            // create a list of the ports we're interested in
            List<sIO> ports = new List<sIO>();

            // speccy screen
            for (int i = SCREEN_BITMAP_BASE; i < SCREEN_ATTRIBUTE_BASE; i++)
            {
                ports.Add(new sIO(i, eAccess.Memory_Write));
            }
            
            // hotkeys
            ports.Add(new sIO("<ctrl><alt>1",SCREENCAPMODE_BYTES, eAccess.KeyPress));
            ports.Add(new sIO("<ctrl><alt>2",SCREENCAPMODE_BUFFER, eAccess.KeyPress));
            ports.Add(new sIO("<ctrl><alt>3",SCREENCAPMODE_SIDEWIZE_TRIGGER, eAccess.KeyPress));
            ports.Add(new sIO("<ctrl><alt>q",CHEATS_SIDEWIZE, eAccess.KeyPress));
            
            // the sidewize hsync port
            ports.Add(new sIO(0x40ff, eAccess.Port_Read));
            
            ScreenCapMode = SCREENCAPMODE_NONE;
            
            return ports;
        }

        // **********************************************************************
        /// <summary>
        ///     Quit the device - free up anything we need to
        /// </summary>
        // **********************************************************************
        public void Quit()
        {
        }

        // **********************************************************************
        /// <summary>
        ///     Called when machine is reset
        /// </summary>
        // **********************************************************************
        public void Reset()
        {
        }

        // **********************************************************************
        /// <summary>
        ///     Called once an emulation FRAME
        /// </summary>
        // **********************************************************************
        public void Tick()
        {
        }

        // **********************************************************************
        /// <summary>
        ///     Key press callback
        /// </summary>
        /// <param name="_id">The registered key ID</param>
        /// <returns>
        ///     True indicates the plugin handled the key
        ///     False indicates someone else can handle it
        /// </returns>
        // **********************************************************************
        public bool KeyPressed(int _id)
        {
            switch (_id)
            {
                case SCREENCAPMODE_BYTES:
                case SCREENCAPMODE_BUFFER:
                    if (ScreenCapMode == _id) {
                        Console.WriteLine("Disabled Screen Capture Mode {0}.", ScreenCapMode);
                        ScreenCapMode = SCREENCAPMODE_NONE;
                        Frame = 0;
                    } else {
                        Array.Clear(ScreenBytes, 0, ScreenBytes.Length);
                        ScreenCapMode = _id;
                        Console.WriteLine("Enabled Screen Capture Mode {0}.", ScreenCapMode);
                    }
                    break;
                case CHEATS_SIDEWIZE:
                    // invincible
                    Console.WriteLine("Sidewize Cheats Activated.");
                    CSpect.Poke(52637, 9);
                    CSpect.Poke(52647, 9);

                    // remove the attrib wait loop as some register
                    // corruption seemns to occur
                    CSpect.Poke(0x9cfe, 0);
                    CSpect.Poke(0x9cff, 0);
                    CSpect.Poke(0x9d00, 0);

                    break;
                case SCREENCAPMODE_SIDEWIZE_TRIGGER:
                    ScreenCapMode = _id;
                    break;
                default:
                    break;
            }
            return true;
        }

        // **********************************************************************
        /// <summary>
        ///     Write a value to one of the registered ports
        /// </summary>
        /// <param name="_port">the port being written to</param>
        /// <param name="_value">the value to write</param>
        // **********************************************************************
        public bool Write(eAccess _type, int _port, byte _value)
        {
            String filename = String.Format("frames/frame_{0}.scr", Frame);
            Frame++;
            
            switch (ScreenCapMode)
            {
                case SCREENCAPMODE_BYTES:
                    ScreenBytes[_port - SCREEN_BITMAP_BASE] = _value;
                    File.WriteAllBytes(filename, ScreenBytes);
                    Console.Write("-");
                    break;
                
                case SCREENCAPMODE_BUFFER:
                    File.WriteAllBytes(filename,CSpect.Peek(SCREEN_BITMAP_BASE, SCREEN_BITMAP_LENGTH + SCREEN_ATTRIBUTE_LENGTH));
                    Console.Write("+");
                    break;
                
                default:
                    break;
            }
            return false;

        }
        
        // **********************************************************************
        /// <summary>
        ///     
        /// </summary>
        /// <param name="_port">Port/Address</param>
        /// <param name="_isvalid"></param>
        /// <returns></returns>
        // **********************************************************************
        public byte Read(eAccess _type, int _port, out bool _isvalid)
        {
            switch (_port)
            {
                // sidewize uses this port to scan for the raster crossing attributes
                case 0x40ff:
                    // turn on direct frame buffer sampling for one screen update 
                    switch (ScreenCapMode)
                    {
                        case SCREENCAPMODE_SIDEWIZE_TRIGGER:
                            KeyPressed(SCREENCAPMODE_BUFFER);
                            break;
                        case SCREENCAPMODE_BUFFER:
                            KeyPressed(SCREENCAPMODE_BUFFER);
                            break;
                        default:
                            break;
                    }
                    _isvalid = true;
                    return 0x40;
                default:
                    break;
            }
            _isvalid = false;
            return 0;
        }
        #endregion
        
    }
}
