using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WarpEngine
{
    public class ShipInfo
    {
        public Part ShipPart { get; set; }
        public float BreakingForce { get; set; }
        public float BreakingTorque { get; set; }
        public float CrashTolerance { get; set; }
        public int PhysicsSignificance { get; set; }
    }
    public class USI_ModuleWarpEngine : PartModule
    {
        [KSPField(guiActive = true, guiName = "Status", guiActiveEditor = false)]
        public string status = "inactive";

        [KSPField]
        public string deployAnimationName = "Engage";

        [KSPField]
        public string warpAnimationName = "WarpField";

        [KSPField] 
        public float WarpFactor = 1.65f;

        [KSPField]
        public float Demasting = 10f;

        [KSPField]
        public float MaxAccelleration = 10000f;

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

        [KSPEvent(guiActive = true, active = true, guiActiveEditor = true, guiName = "Toggle Bubble Guide")]
        public void ToggleBubbleGuide()
        {
            //Affects both the icon and the ship it turns out
            foreach (var gobj in GameObject.FindObjectsOfType<GameObject>())
            {
                if(gobj.name == "EditorWarpBubble")
                    gobj.renderer.enabled = !gobj.renderer.enabled;
            }
        }

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


        private const int LIGHTSPEED = 299792458;
        private const int SUBLIGHT_MULT = 40;
        private const int SUBLIGHT_POWER = 5;
        private const double SUBLIGHT_THROTTLE = .3d;
        private StartState _state;
        private double CurrentSpeed;
        private List<ShipInfo> _shipParts;


        public override void OnStart(StartState state)
        {
            try
            {
                DeployAnimation[deployAnimationName].layer = 3;
                WarpAnimation[warpAnimationName].layer = 4;
                _state = state;
                if (_state == StartState.Editor)
                    return;
                CheckBubbleDeployment(1000);
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
                CheckBubbleDeployment(1000);
                base.OnLoad(node);
            }
            catch (Exception ex)
            {
                print(String.Format("[WARP] Error in OnLoad - {0}", ex.Message));
            }
        }

        private void SetPartState(bool stiffenJoints)
        {
            if (stiffenJoints)
            {
                //Stiffen Joints
                _shipParts = new List<ShipInfo>();
                foreach (var vp in vessel.parts)
                {
                    print("[WARP] Stiffening " + vp.name);
                    _shipParts.Add(new ShipInfo
                                   {
                                       ShipPart = vp,
                                       BreakingForce = vp.breakingForce,
                                       BreakingTorque = vp.breakingTorque,
                                       CrashTolerance = vp.crashTolerance
                                   });
                    vp.breakingForce = Mathf.Infinity;
                    vp.breakingTorque = Mathf.Infinity;
                    vp.crashTolerance = Mathf.Infinity;
                }
                
            }

            else
            {
                //Stop vessel
                vessel.rigidbody.AddTorque(-vessel.rigidbody.angularVelocity);
                //Reset part state
                if (_shipParts != null)
                {
                    foreach (var sp in _shipParts)
                    {
                        if (vessel.parts.Contains(sp.ShipPart))
                        {
                            print("[WARP] Relaxing " + sp.ShipPart.name);
                            sp.ShipPart.rigidbody.AddTorque(-sp.ShipPart.rigidbody.angularVelocity);
                            sp.ShipPart.breakingForce = sp.BreakingForce;
                            sp.ShipPart.breakingTorque = sp.BreakingTorque;
                            sp.ShipPart.crashTolerance = sp.CrashTolerance;
                        }
                    }
                }
            }
        }

        public void FixedUpdate()
        {
            try
            {
                if (vessel == null || _state == StartState.Editor) return;
                var eModule = part.FindModuleImplementing<ModuleEngines>();
                if (IsDeployed != eModule.getIgnitionState)
                {
                    IsDeployed = eModule.getIgnitionState;
                    CheckBubbleDeployment(3);
                    SetPartState(eModule.getIgnitionState);
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

                    //OH NO FLAMEOUT!
                    if (eModule.flameout)
                    {
                        BubbleCollapse(eModule.currentThrottle);
                        FlightInputHandler.state.mainThrottle = 0;
                        IsDeployed = false;
                        return;
                    }

                    PlayWarpAnimation(1 + (eModule.currentThrottle * (WarpFactor * 3)));

                    //Start by adding in our subluminal speed which is exponential
                    double lowerThrottle = (double)eModule.currentThrottle * SUBLIGHT_MULT;
                    double distance = Math.Pow(lowerThrottle, SUBLIGHT_POWER);
                    
                    //Then if throttle is over our threshold, go linear
                    if (eModule.currentThrottle > SUBLIGHT_THROTTLE)
                    {
                        //How much headroon do we have
                        double maxSpeed = (LIGHTSPEED/50*WarpFactor) - distance;
                        //How much of this can we use?
                        var upperThrottle = eModule.currentThrottle - SUBLIGHT_THROTTLE;
                        //How much of this headroom have we used?
                        var throttlePercent = upperThrottle/(1 - SUBLIGHT_THROTTLE);
                        //Add it to our current throttle calculation
                        var additionalDistance = maxSpeed*throttlePercent;
                        distance += additionalDistance;
                    }

                    //Take into acount safe accelleration/decelleration
                    if (distance > CurrentSpeed + MaxAccelleration)
                        distance = CurrentSpeed + MaxAccelleration;
                    if (distance < CurrentSpeed - MaxAccelleration)
                        distance = CurrentSpeed - MaxAccelleration;

                    CurrentSpeed = distance;

                    double c = (distance * 50) / LIGHTSPEED;
                    status = String.Format("{1:n0} m/s [{0:0}%c]", c*100f, distance * 50);

                    //If we are below our throttle, we kill all angular velocity.
                    if (eModule.currentThrottle <= MinThrottle)
                    {
                        foreach (var p in vessel.parts)
                        {
                            p.rigidbody.angularVelocity *= .99f;
                        }
                    }
                    else
                    {
                        //Otherwise we reposition as normal
                        var ps = vessel.transform.position + (transform.up * (float)distance);
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
                    double distance = Vector3d.Distance(posBubble, posPart);
                    if (distance > BubbleSize)
                    {
                        print("[WARP] Decoupling Part " + p.name);
                        p.decouple();
                    }
                }
            }
            catch (Exception)
            {
                print("[WARP] ERROR IN DecoupleBubbleParts");
            }
        }

        private void CheckBubbleDeployment(int speed)
        {
            try
            {
                //Turn off guide if there
                foreach (var gobj in GameObject.FindObjectsOfType<GameObject>())
                {
                    if (gobj.name == "EditorWarpBubble")
                        gobj.renderer.enabled = false;
                }

               
                if (IsDeployed)
                {
                    SetDeployedState(speed);
                }
                else
                {
                    SetRetractedState(-speed);
                    status = "inactive";
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
