﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using emulatorLauncher.Tools;

namespace emulatorLauncher
{
    partial class YuzuGenerator : Generator
    {
        private void CreateControllerConfiguration(IniFile ini)
        {
            if (Program.SystemConfig.isOptSet("disableautocontrollers") && Program.SystemConfig["disableautocontrollers"] == "1")
                return;

            foreach (var controller in this.Controllers.OrderBy(i => i.PlayerIndex))
                ConfigureInput(controller, ini);
        }

        private void ConfigureInput(Controller controller, IniFile ini)
        {
            if (controller == null || controller.Config == null)
                return;

            if (controller.IsKeyboard)
                ConfigureKeyboard(controller, ini);
            else
                ConfigureJoystick(controller, ini);
        }

        private void ConfigureJoystick(Controller controller, IniFile ini)
        {
            if (controller == null)
                return;

            var cfg = controller.Config;
            if (cfg == null)
                return;

            var guid = controller.GetSdlGuid(_sdlVersion);

            // Yuzu deactivates RAWINPUT with SDL_SetHint(SDL_HINT_JOYSTICK_RAWINPUT, 0) when enable_raw_input is set to false (default value) 
            // Convert Guid to XInput
            if (ini.GetValue("Controls", "enable_raw_input\\default") != "false" || ini.GetValue("Controls", "enable_raw_input") == "false")
            {
                if (controller.SdlWrappedTechID == SdlWrappedTechId.RawInput && controller.XInput != null)
                    guid = guid.ToXInputGuid(controller.XInput.SubType);
            }

            var yuzuGuid = guid.ToString().ToLowerInvariant();

            int index = Program.Controllers
                    .GroupBy(c => c.Guid.ToLowerInvariant())
                    .Where(c => c.Key == controller.Guid.ToLowerInvariant())
                    .SelectMany(c => c)
                    .OrderBy(c => SdlGameController.GetControllerIndex(c))
                    .ToList()
                    .IndexOf(controller);

            string player = "player_" + (controller.PlayerIndex - 1) + "_";

            ini.WriteValue("Controls", player + "type" + "\\default", "true");
            ini.WriteValue("Controls", player + "type", "0");
            ini.WriteValue("Controls", player + "connected" + "\\default", "false");
            ini.WriteValue("Controls", player + "connected", "true");

            //Vibration settings
            ini.WriteValue("Controls", player + "vibration_enabled" + "\\default", "true");
            ini.WriteValue("Controls", player + "vibration_enabled", "true");
            ini.WriteValue("Controls", player + "left_vibration_device" + "\\default", "true");
            ini.WriteValue("Controls", player + "right_vibration_device" + "\\default", "true");
            ini.WriteValue("Controls", "enable_accurate_vibrations" + "\\default", "false");
            ini.WriteValue("Controls", "enable_accurate_vibrations", "true");

            //vibration strength for XInput = 70
            if (controller.IsXInputDevice)
            {
                ini.WriteValue("Controls", player + "vibration_strength" + "\\default", "false");
                ini.WriteValue("Controls", player + "vibration_strength", "70");
            }
            else
            {
                ini.WriteValue("Controls", player + "vibration_strength" + "\\default", "true");
                ini.WriteValue("Controls", player + "vibration_strength", "100");
            }

            //Manage motion
            if (Program.SystemConfig.isOptSet("yuzu_enable_motion") && Program.SystemConfig.getOptBoolean("yuzu_enable_motion"))
            {
                ini.WriteValue("Controls", "motion_device" + "\\default", "true");
                ini.WriteValue("Controls", "motion_device", "\"" + "engine:motion_emu,update_period:100,sensitivity:0.01" + "\"");
                ini.WriteValue("Controls", "motion_enabled" + "\\default", "true");
                ini.WriteValue("Controls", "motion_enabled", "true");
            }
            else
            {
                ini.WriteValue("Controls", "motion_device" + "\\default", "true");
                ini.WriteValue("Controls", "motion_device", "[empty]");
                ini.WriteValue("Controls", "motion_enabled" + "\\default", "false");
                ini.WriteValue("Controls", "motion_enabled", "false");
            }

            //XInput controllers do not have motion - disable for XInput players, else use default sdl motion engine from the controller
            if (!controller.IsXInputDevice)
            {
                ini.WriteValue("Controls", player + "motionleft" + "\\default", "false");
                ini.WriteValue("Controls", player + "motionleft", "\"" + "engine:sdl,motion:0,port:" + index + ", guid:" + yuzuGuid + "\"");
                ini.WriteValue("Controls", player + "motionright" + "\\default", "false");
                ini.WriteValue("Controls", player + "motionright", "\"" + "engine:sdl,motion:0,port:" + index + ",guid:" + yuzuGuid + "\"");
            }
            else
            {
                ini.WriteValue("Controls", player + "motionleft" + "\\default", "true");
                ini.WriteValue("Controls", player + "motionleft", "[empty]");
                ini.WriteValue("Controls", player + "motionright" + "\\default", "true");
                ini.WriteValue("Controls", player + "motionright", "[empty]");
            }

            foreach (var map in Mapping)
            {
                string name = player + map.Value;

                string cvalue = FromInput(controller, cfg[map.Key], yuzuGuid, index);

                if (string.IsNullOrEmpty(cvalue))
                {
                    ini.WriteValue("Controls", name + "\\default", "false");
                    ini.WriteValue("Controls", name, "[empty]");
                }
                else
                {
                    ini.WriteValue("Controls", name + "\\default", "false");
                    ini.WriteValue("Controls", name, "\"" + cvalue + "\"");
                }
            }

            ini.WriteValue("Controls", player + "button_sl" + "\\default", "false");
            ini.WriteValue("Controls", player + "button_sl", "[empty]");
            ini.WriteValue("Controls", player + "button_sr" + "\\default", "false");
            ini.WriteValue("Controls", player + "button_sr", "[empty]");
            ini.WriteValue("Controls", player + "button_home" + "\\default", "false");
            ini.WriteValue("Controls", player + "button_home", "[empty]");
            ini.WriteValue("Controls", player + "button_screenshot" + "\\default", "false");
            ini.WriteValue("Controls", player + "button_screenshot", "[empty]");

            ProcessStick(controller, player, "lstick", ini, yuzuGuid, index);
            ProcessStick(controller, player, "rstick", ini, yuzuGuid, index);
        }

        private string FromInput(Controller controller, Input input, string guid, int index)
        {
            if (input == null)
                return null;

            string value = "engine:sdl,port:" + index + ",guid:" + guid;

            if (input.Type == "button")
                value += ",button:" + input.Id;
            else if (input.Type == "hat")
                value += ",direction:" + input.Name.ToString() + ",hat:" + input.Id;
            else if (input.Type == "axis")
            {
                //yuzu sdl implementation uses "2" as axis value for left trigger for XInput
                if (controller.IsXInputDevice)
                {
                    long newID = input.Id;
                    if (input.Id == 4)
                        newID = 2;
                    value = "engine:sdl,port:" + index + ",axis:" + newID + ",guid:" + guid + ",threshold:0.500000" + ",invert:+";
                }
                else
                    value = "engine:sdl,port:" + index + ",axis:" + input.Id + ",guid:" + guid + ",threshold:0.500000" + ",invert:+";
            }

            return value;
        }

        private void ProcessStick(Controller controller, string player, string stickName, IniFile ini, string guid, int index)
        {
            var cfg = controller.Config;

            string name = player + stickName;

            var leftVal = cfg[stickName == "lstick" ? InputKey.joystick1left : InputKey.joystick2left];
            var topVal = cfg[stickName == "lstick" ? InputKey.joystick1up : InputKey.joystick2up];

            if (leftVal != null && topVal != null && leftVal.Type == topVal.Type && leftVal.Type == "axis")
            {
                long yuzuleftval = leftVal.Id;
                long yuzutopval = topVal.Id;

                //yuzu sdl implementation uses 3 and 4 for right stick axis values with XInput
                if (controller.IsXInputDevice && stickName == "rstick")
                {
                    yuzuleftval = 3;
                    yuzutopval = 4;
                }

                string value = "engine:sdl," + "axis_x:" + yuzuleftval + ",port:" + index + ",guid:" + guid + ",axis_y:" + yuzutopval + ",deadzone:0.150000,range:1.000000";

                ini.WriteValue("Controls", name + "\\default", "false");
                ini.WriteValue("Controls", name, "\"" + value + "\"");
            }

            else
            {
                ini.WriteValue("Controls", name + "\\default", "false");
                ini.WriteValue("Controls", name, "[empty]");
            }
        }

        private static void ConfigureKeyboard(Controller controller, IniFile ini)
        {
            if (controller == null)
                return;

            InputConfig keyboard = controller.Config;
            if (keyboard == null)
                return;

            string player = "player_" + (controller.PlayerIndex - 1) + "_";

            ini.WriteValue("Controls", player + "type" + "\\default", "true");
            ini.WriteValue("Controls", player + "type", "0");
            ini.WriteValue("Controls", player + "connected" + "\\default", "true");
            ini.WriteValue("Controls", player + "connected", "true");
            ini.WriteValue("Controls", player + "vibration_enabled" + "\\default", "true");
            ini.WriteValue("Controls", player + "vibration_enabled", "true");
            ini.WriteValue("Controls", player + "left_vibration_device" + "\\default", "true");
            ini.WriteValue("Controls", player + "right_vibration_device" + "\\default", "true");
            ini.WriteValue("Controls", "enable_accurate_vibrations" + "\\default", "true");
            ini.WriteValue("Controls", "enable_accurate_vibrations", "false");
            ini.WriteValue("Controls", player + "vibration_strength" + "\\default", "true");
            ini.WriteValue("Controls", player + "vibration_strength", "100");
            ini.WriteValue("Controls", player + "button_a\\default", "true");
            ini.WriteValue("Controls", player + "button_a", "\"" + "engine:keyboard,code:67,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_b\\default", "true");
            ini.WriteValue("Controls", player + "button_b", "\"" + "engine:keyboard,code:88,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_x\\default", "true");
            ini.WriteValue("Controls", player + "button_x", "\"" + "engine:keyboard,code:86,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_y\\default", "true");
            ini.WriteValue("Controls", player + "button_y", "\"" + "engine:keyboard,code:90,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_lstick\\default", "true");
            ini.WriteValue("Controls", player + "button_lstick", "\"" + "engine:keyboard,code:70,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_rstick\\default", "true");
            ini.WriteValue("Controls", player + "button_rstick", "\"" + "engine:keyboard,code:71,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_l\\default", "true");
            ini.WriteValue("Controls", player + "button_l", "\"" + "engine:keyboard,code:81,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_r\\default", "true");
            ini.WriteValue("Controls", player + "button_r", "\"" + "engine:keyboard,code:69,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_zl\\default", "true");
            ini.WriteValue("Controls", player + "button_zl", "\"" + "engine:keyboard,code:82,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_zr\\default", "true");
            ini.WriteValue("Controls", player + "button_zr", "\"" + "engine:keyboard,code:84,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_plus\\default", "true");
            ini.WriteValue("Controls", player + "button_plus", "\"" + "engine:keyboard,code:77,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_minus\\default", "true");
            ini.WriteValue("Controls", player + "button_minus", "\"" + "engine:keyboard,code:78,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_dleft\\default", "true");
            ini.WriteValue("Controls", player + "button_dleft", "\"" + "engine:keyboard,code:16777234,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_dup\\default", "true");
            ini.WriteValue("Controls", player + "button_dup", "\"" + "engine:keyboard,code:16777235,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_dright\\default", "true");
            ini.WriteValue("Controls", player + "button_dright", "\"" + "engine:keyboard,code:16777236,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_ddown\\default", "true");
            ini.WriteValue("Controls", player + "button_ddown", "\"" + "engine:keyboard,code:16777237,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_sl\\default", "true");
            ini.WriteValue("Controls", player + "button_sl", "\"" + "engine:keyboard,code:81,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_sr\\default", "true");
            ini.WriteValue("Controls", player + "button_sr", "\"" + "engine:keyboard,code:69,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_home\\default", "true");
            ini.WriteValue("Controls", player + "button_home", "\"" + "engine:keyboard,code:0,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "button_screenshot\\default", "true");
            ini.WriteValue("Controls", player + "button_screenshot", "\"" + "engine:keyboard,code:0,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "lstick\\default", "false");
            ini.WriteValue("Controls", player + "lstick", "\"" + "engine:analog_from_button,up:engine$0keyboard$1code$087$1toggle$00,left:engine$0keyboard$1code$065$1toggle$00,modifier:engine$0keyboard$1code$016777248$1toggle$00,down:engine$0keyboard$1code$083$1toggle$00,right:engine$0keyboard$1code$068$1toggle$00" + "\"");
            ini.WriteValue("Controls", player + "rstick\\default", "false");
            ini.WriteValue("Controls", player + "rstick", "\"" + "engine:mouse,axis_x:0,axis_y:1,threshold:0.500000,range:1.000000,deadzone:0.000000" + "\"");
            ini.WriteValue("Controls", player + "motionleft\\default", "true");
            ini.WriteValue("Controls", player + "motionleft", "\"" + "engine:keyboard,code:55,toggle:0" + "\"");
            ini.WriteValue("Controls", player + "motionright\\default", "true");
            ini.WriteValue("Controls", player + "motionright", "\"" + "engine:keyboard,code:56,toggle:0" + "\"");
        }

        static Dictionary<string, string> DefKeys = new Dictionary<string, string>()
        {
            { "button_a", "engine:keyboard,code:67,toggle:0" },
            { "button_b","engine:keyboard,code:88,toggle:0" },
            { "button_x","engine:keyboard,code:86,toggle:0" },
            { "button_y","engine:keyboard,code:90,toggle:0" },
            { "button_lstick","engine:keyboard,code:70,toggle:0" },
            { "button_rstick","engine:keyboard,code:71,toggle:0" },
            { "button_l","engine:keyboard,code:81,toggle:0" },
            { "button_r","engine:keyboard,code:69,toggle:0" },
            { "button_zl","engine:keyboard,code:82,toggle:0" },
            { "button_zr","engine:keyboard,code:84,toggle:0" },
            { "button_plus","engine:keyboard,code:77,toggle:0" },
            { "button_minus","engine:keyboard,code:78,toggle:0" },
            { "button_dleft","engine:keyboard,code:16777234,toggle:0" },
            { "button_dup","engine:keyboard,code:16777235,toggle:0" },
            { "button_dright","engine:keyboard,code:16777236,toggle:0" },
            { "button_ddown","engine:keyboard,code:16777237,toggle:0" },
            { "button_sl","engine:keyboard,code:81,toggle:0" },
            { "button_sr","engine:keyboard,code:69,toggle:0" },
            { "button_home","engine:keyboard,code:0,toggle:0" },
            { "button_screenshot","engine:keyboard,code:0,toggle:0" },
            { "lstick","engine:analog_from_button,up:engine$0keyboard$1code$087$1toggle$00,left:engine$0keyboard$1code$065$1toggle$00,modifier:engine$0keyboard$1code$016777248$1toggle$00,down:engine$0keyboard$1code$083$1toggle$00,right:engine$0keyboard$1code$068$1toggle$00,modifier_scale:0.500000" },
            { "rstick","engine:analog_from_button,up:engine$0keyboard$1code$073$1toggle$00,left:engine$0keyboard$1code$074$1toggle$00,modifier:engine$0keyboard$1code$00$1toggle$00,down:engine$0keyboard$1code$075$1toggle$00,right:engine$0keyboard$1code$076$1toggle$00,modifier_scale:0.500000" }
        };

        static InputKeyMapping Mapping = new InputKeyMapping()
        {
            { InputKey.select,          "button_minus" },
            { InputKey.start,           "button_plus" },

            { InputKey.b,               "button_a" },
            { InputKey.a,               "button_b" },

            { InputKey.y,               "button_y" },
            { InputKey.x,               "button_x" },

            { InputKey.up,              "button_dup" },
            { InputKey.down,            "button_ddown" },
            { InputKey.left,            "button_dleft" },
            { InputKey.right,           "button_dright" },

            { InputKey.pageup,          "button_l" },
            { InputKey.pagedown,        "button_r" },

            { InputKey.l2,              "button_zl" },
            { InputKey.r2,              "button_zr"},

            { InputKey.l3,              "button_lstick"},
            { InputKey.r3,              "button_rstick"},
        };
    }
}