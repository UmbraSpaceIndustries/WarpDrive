using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WarpEngine
{
    public class USI_ModuleWarpEngine : PartModule
    {
        [KSPField(guiActive = true, guiName = "Status", guiActiveEditor = false)]
        public string status = "inactive";

        [KSPField]
        public string deployAnimationName = "Engage";

        [KSPField]
        public string warpAnimationName = "WarpField";

        [KSPField]
        public float WarpFactor = 5f;

        [KSPField]
        public float Demasting = 10f;


        [KSPField]
        public float MinThrottle = 0.05f;

        [KSPField(isPersistant = true)] 
        public bool IsDeployed = false;

        [KSPField]
        public int DisruptRange = 2000;
        
        [KSPField]
        public int BubbleSize = 20;
        
        [KSPField]
        public int MinAltitude = 50000;

        public Animation DeployAnimation
        {
            get
            {
                try
                {
                    return part.FindModelAnimators(deployAnimationName)[0];
                }
                catch (Exception)
                {
                    print("[WARP] ERROR IN GetDeployAnimation");
                    return null;
                }
            }
        }

        public Animation WarpAnimation
        {
            get
            {
                try
                {
                    return part.FindModelAnimators(warpAnimationName)[0];
                }
                catch (Exception)
                {
                    print("[WARP] ERROR IN GetWarpAnimation");
                    return null;
                }
            }
        }

        
        public const int LIGHTSPEED = 299792458;
        private StartState _state;

        public override void OnStart(StartState state)
        {
            try
            {
                DeployAnimation[deployAnimationName].layer = 3;
                WarpAnimation[warpAnimationName].layer = 4;
                _state = state;
                if (_state == StartState.Editor) return;
                CheckBubbleDeployment();
                base.OnStart(state);
            }
            catch (Exception ex)
            {
                print(String.Format("[WARP] Error in OnStart - {0}", ex.Message));
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            try
            {
                if (_state == StartState.Editor) return;
                part.force_activate();
                CheckBubbleDeployment();
                base.OnLoad(node);
            }
            catch (Exception ex)
            {
                print(String.Format("[WARP] Error in OnLoad - {0}", ex.Message));
            }
        }

        public override void OnFixedUpdate()
        {
            try
            {
                if (vessel == null) return;
                var eModule = part.FindModuleImplementing<ModuleEngines>();
                if (IsDeployed != eModule.getIgnitionState)
                {
                    IsDeployed = eModule.getIgnitionState;
                    CheckBubbleDeployment();
                }

                if (IsDeployed)
                {
                    //Failsafe
                    if (vessel.altitude < MinAltitude)
                    {
                        eModule.Shutdown();
                        return;
                    }
                    //Snip partsx
                    DecoupleBubbleParts();
                    //Other ships
                    DestroyNearbyShips();

                    var throttleMultiplier = eModule.currentThrottle * 10;

                    //OH NO FLAMEOUT!
                    if (eModule.flameout)
                    {
                        BubbleCollapse(eModule.currentThrottle);
                        FlightInputHandler.state.mainThrottle = 0;
                        IsDeployed = false;
                        return;
                    }

                    PlayWarpAnimation(1 + (eModule.currentThrottle * WarpFactor));

                    if (eModule.currentThrottle > MinThrottle)
                    {
                        var distance = (float)Math.Pow(WarpFactor, (throttleMultiplier));
                        float c = (distance * 50) / LIGHTSPEED;
                        status = String.Format("speed: {0:0.0000}c", c);
                        var ps = part.transform.position + (transform.up * distance);
                        part.vessel.SetPosition(ps);
                    }
                }
            }
            catch (Exception ex)
            {
                print(String.Format("[WARP] Error in OnFixedUpdate - {0}", ex.Message));
            }
        }


        private void BubbleCollapse(float speed)
        {
            var r = new System.Random();
            foreach (var p in vessel.parts)
            {
                var expl = r.Next(0, 100);
                if (expl <= speed * 100)
                {
                    if(p.mass <= (Demasting * speed) && p.children.Count == 0)
                    p.explode();
                }
            }
        }

        private void PlayWarpAnimation(float speed)
        {
            try
            {
                if (!WarpAnimation.IsPlaying(warpAnimationName))
                {
                    WarpAnimation[warpAnimationName].speed = speed;
                    WarpAnimation.Play(warpAnimationName);
                }
            }
            catch (Exception)
            {
                print("[WARP] ERROR IN PlayWarpAnimation");
            }
        }
        private void DecoupleBubbleParts()
        {
            try
            {
                foreach (var p in vessel.parts)
                {
                    var posPart = p.partTransform.position;
                    var posBubble = part.partTransform.position;
                    var distance = Vector3d.Distance(posBubble, posPart);
                    if (distance > BubbleSize)
                    {
                       p.decouple();
                    }
                }
            }
            catch (Exception)
            {
                print("[WARP] ERROR IN DecoupleBubbleParts");
            }
        }

        private void CheckBubbleDeployment()
        {
            try
            {
                if (_state == StartState.Editor) return;
                if (IsDeployed)
                {
                    SetDeployedState(1000);
                }
                else
                {
                    SetRetractedState(-1000);
                }
            }
            catch (Exception)
            {
                print("[WARP] ERROR IN CheckBubbleDeployment");
            }
        }

        private void SetRetractedState(int speed)
        {
            try
            {
                IsDeployed = false;
                PlayDeployAnimation(speed);
            }
            catch (Exception)
            {
                print("[WARP] ERROR IN SetRetractedState");
            }
        }

        private void SetDeployedState(int speed)
        {
            try
            {
                IsDeployed = true;
                PlayDeployAnimation(speed);
            }
            catch (Exception)
            {
                print("[WARP] ERROR IN SetDeployedState");
            }
        }

        private void PlayDeployAnimation(int speed)
        {
            try
            {
                if (speed < 0)
                {
                    DeployAnimation[deployAnimationName].time = DeployAnimation[deployAnimationName].length;
                }
                DeployAnimation[deployAnimationName].speed = speed;
                DeployAnimation.Play(deployAnimationName);
            }
            catch (Exception)
            {
                print("[WARP] ERROR IN PlayDeployAnimation");
            }
        }



        private void DestroyNearbyShips()
        {
            try
            {
                var ships = GetNearbyVessels(DisruptRange, false);
                foreach (var s in ships)
                {
                    foreach (var p in s.parts)
                    {
                        p.explode();
                    }
                }
            }
            catch (Exception)
            {
                print("[WARP] ERROR IN DestroyNearbyShips");
            }
        }

        private List<Vessel> GetNearbyVessels(int range, bool includeSelf)
        {
            try
            {
                var vessels = new List<Vessel>();
                foreach (var v in FlightGlobals.Vessels.Where(
                    x => x.mainBody == vessel.mainBody))
                {
                    if (v == vessel && !includeSelf) continue;
                    var posCur = vessel.GetWorldPos3D();
                    var posNext = v.GetWorldPos3D();
                    var distance = Vector3d.Distance(posCur, posNext);
                    if (distance < range)
                    {
                        vessels.Add(v);
                    }
                }
                return vessels;
            }
            catch (Exception ex)
            {
                print(String.Format("[WARP] - ERROR in GetNearbyVessels - {0}", ex.Message));
                return new List<Vessel>();
            }
        }
    }
}
