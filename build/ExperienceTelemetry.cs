using System;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace AbyssalCrab.Runtime
{
    // Read once per existing session.inspect. Never installs a hook, targets a creature,
    // changes a weapon, writes player input or affects the per-frame aiming path.
    public static class ExperienceTelemetry
    {
        private static AccessTools.FieldRef<Weapon,WeaponInfo> _info;
        private static Func<Weapon,float> _speed;
        public static JToken Describe(JToken session)
        {
            var root=session as JObject;if(root==null)return session;
            var telemetry=new JObject{["available"]=false};root["experienceTelemetry"]=telemetry;
            try
            {
                var player=Player.LocalPlayer;
                if(!player||!player.Holding){telemetry["reason"]="no-player";return session;}
                var held=player.Holding.HeldItem;var weapon=held?held.Weapon:null;
                if(!weapon){telemetry["reason"]="no-weapon";return session;}
                if(_info==null)_info=AccessTools.FieldRefAccess<Weapon,WeaponInfo>("_weaponInfo");
                if(_speed==null)_speed=AccessTools.MethodDelegate<Func<Weapon,float>>(AccessTools.Method(typeof(Weapon),"GetProjectileSpeed"),null,true);
                var info=_info(weapon);float speed=_speed(weapon);
                if(info==null||float.IsNaN(speed)||float.IsInfinity(speed)||speed<0){telemetry["reason"]="invalid-weapon-data";return session;}
                telemetry["available"]=true;telemetry["name"]=held.name;telemetry["speed"]=speed;
                telemetry["gravity"]=info.ProjectileGravity;telemetry["ads"]=weapon.IsAds;
                telemetry["reason"]="current-weapon";
            }
            catch(Exception error){telemetry["reason"]="weapon-data-unavailable";telemetry["error"]=error.GetType().Name;}
            return session;
        }
    }
}
