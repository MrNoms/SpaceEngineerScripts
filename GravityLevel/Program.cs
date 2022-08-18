using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram {
        private const string NO_COCKS = "No usable control blocks found.";
        private const string NO_MAIN = "No main control blocks found.";
        private const string NO_GYROS = "No overridden gyroscopes found.";

        private const UpdateFrequency REFRESH = UpdateFrequency.Update10;
        private const UpdateFrequency STOP = UpdateFrequency.None;

        private bool _debugging;
        private bool _running;

        private List<IMyShipController> _controls;
        private IMyShipController _main;
        private Vector3D _grav, _down;

        private Dictionary<IMyGyro, Dictionary<string, float>> _gyroSavedData;
        private List<IMyGyro> _gyros;
        private List<IMyGyro> _plumbs;
        private Vector3D _target, _actuation;

        public Program() {
            _controls = new List<IMyShipController>();
            _gyros = new List<IMyGyro>();
            _plumbs = new List<IMyGyro>();
            _gyroSavedData = new Dictionary<IMyGyro, Dictionary<string, float>>();
            _target = new Vector3D(0, 0, 0);
            _actuation = new Vector3D(0, 0, 0);

            GridTerminalSystem.GetBlocksOfType(_controls, controller => controller.IsSameConstructAs(Me));
            GridTerminalSystem.GetBlocksOfType(_gyros, gyro => gyro.IsSameConstructAs(Me));
        }

        public void Save() { }

        public void Main(string argument, UpdateType updateSource) {
            if (!_powerButton(ref updateSource) || !_gravityLevel())
                Echo("System Offline");

            /*
             Exploiting boolean short-circuiting while Gravity Level is always true
                _powerButton is false, script is offline. Condition will
                get a first true, T || F does not evaluate F, prevents
                Gravity Level's evalution and states "System Offline"
                
                _powerButton is true, script needs to run. Condition will
                get a first false, F || F must evaluate both F, forcing
                Gravity Level's evaluation. Since the condition turns out
                to be false, script will not state "System Offline".
            
             This only serves prevent the branch predition of the if-else construct*/
        }

        private bool _powerButton(ref UpdateType update)
        {
            _debugging = update == UpdateType.Terminal;

            if((update & (UpdateType.Trigger | UpdateType.Terminal)) != 0)
            {
                Echo($"Executing by {update} " + (_debugging ? "and debugging" : "") + "\n");

                _running = Runtime.UpdateFrequency == REFRESH;
                if(!_running)
                {
                    Echo("Attempting to start\n");

                    _plumbs = _gyros.FindAll(g => _checkOverride(g));
                    if(_plumbs.Count == 0)
                    {
                        Echo(NO_GYROS); return false;
                    }

                    _main = _controls.Find(c => c.IsMainCockpit && c.IsUnderControl);
                    if(_main == null)
                    {
                        Echo(NO_MAIN);
                        _main = _controls.Find(c => c.IsUnderControl);
                    }

                    if (_main == null)
                    {
                        Echo(NO_COCKS); _running = true;
                    }
                    else Echo($"Using {_main.CustomName}");

                    Echo($"Overridden Gyros: {_plumbs.Count}\n" +
                        $"Updating at: {Runtime.UpdateFrequency}\n");
                    if (_debugging)
                    {
                        Runtime.UpdateFrequency = STOP;
                        if (_main != null) _gravityLevel();
                    }
                    else Runtime.UpdateFrequency = REFRESH;
                }

                if(_running || _debugging)
                {
                    Echo("Attempting to end\n");
                    _plumbs.ForEach(g => _revertGyro(g));
                    _gyroSavedData.Clear();
                    _plumbs.Clear();
                    Runtime.UpdateFrequency = STOP;
                    return false;
                }
            }

            return false;
        }

        private bool _checkOverride(IMyGyro g)
        {
            Dictionary<string, float> gData;

            if(g.GyroOverride)
            {
                gData = new Dictionary<string, float>();
                gData.Add("Yaw", g.Yaw);
                gData.Add("Pitch", g.Pitch);
                gData.Add("Roll", g.Roll);

                _gyroSavedData.Add(g, gData);

                g.Yaw = 0; g.Pitch = 0; g.Roll = 0;

                Echo($"Overriden gyro {g.CustomName} Pitch, Roll, Yaw saved {gData["Pitch"]}, {gData["Roll"]}, {gData["Yaw"]}");
            }

            return g.GyroOverride;
        }

        private void _revertGyro(IMyGyro g)
        {
            g.Yaw = _gyroSavedData[g]["Yaw"];
            g.Pitch = _gyroSavedData[g]["Pitch"];
            g.Roll = _gyroSavedData[g]["Roll"];
            g.GyroOverride = false;

            Echo($"Overridden {g.CustomName} reloaded Pitch, Roll, Yaw: {g.Pitch}, {g.Roll}, {g.Yaw}");
        }

        private bool _gravityLevel()
        {
            _grav = Vector3D.Normalize(_main.GetTotalGravity());
            _down = _main.WorldMatrix.Down;

            //Change to craft down vector relative to World Frame
            _target = _grav - _down;

            //Change to craft down vector relative to Control Block reference frame
            _target = Vector3D.TransformNormal(
                _target,
                MatrixD.Transpose(_main.WorldMatrix.GetOrientation()));

            Echo($"Gravity Vector:\n" +
                $"{_grav}\n" +
                $"Down Vector:\n" +
                $"{_down}\n" +
                $"Difference relative to Cockpit:\n" +
                $"{_target}");

            //Positive X-components require negative/CCW roll about Backward/Z-axis (Not based on right hand rule)
            //Positive Z-components require negative/Nost downward pitch about Right/X-axis
            //Input on (Y)aw axis needs to match output on Up/Y-axis

            _target.X = _target.Z;
            _target.Z = -_target.Y;
            _target.Y = _main.RotationIndicator.Y;

            Echo($"Target relative to Control Frame:\n" +
                $"Pitch: {_target.X}\n" +
                $"Yaw: {_target.Y}\n" +
                $"Roll: {_target.Z}\n");

            _plumbs.ForEach(gyro => _calculatActuation(gyro));

            return true;
        }

        private void _calculatActuation(IMyGyro g)
        {
            //Required force relative to gyroscope reference frame
            _actuation = Vector3D.TransformNormal(
                _target,
                MatrixD.Multiply(
                    _main.WorldMatrix.GetOrientation(),
                    MatrixD.Transpose(g.WorldMatrix.GetOrientation())));

            g.Pitch = (float)_actuation.X;
            g.Yaw = (float)_actuation.Y;
            g.Roll = (float)_actuation.Z;

            Echo($"{g.CustomName} actuation set to\n" +
                $"Pitch: {g.Pitch}\n" +
                $"Roll: {g.Roll}\n" +
                $"Yaw: {g.Yaw}");
        }
    }
}
