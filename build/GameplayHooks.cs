using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AbyssalCrab.Runtime
{
    public sealed class GameplayHook
    {
        public MethodInfo Original, Prefix, Postfix, Finalizer;
    }

    public static class GameplayHooks
    {
        private static GameplayHook Hook(Type handlers, Type type, string name, string prefix, string postfix, string finalizer = null)
        {
            return new GameplayHook { Original = AccessTools.Method(type,name) ?? throw new MissingMethodException(type.Name,name),
                Prefix = Handler(handlers,prefix), Postfix = Handler(handlers,postfix), Finalizer = Handler(handlers,finalizer) };
        }
        private static MethodInfo Handler(Type handlers, string name) => name == null ? null : handlers.GetMethod(name) ?? throw new MissingMethodException(name);
        public static List<GameplayHook> Plan(int group, Type handlers)
        {
            var plan = new List<GameplayHook>();
            if (group == 1) plan.Add(Hook(handlers,typeof(PlayerMovement),"FixedUpdate",null,"DashFixed"));
            else if (group == 2)
            {
                plan.Add(Hook(handlers,typeof(Creature),"LocalHit","CreatureHit",null));
                plan.Add(Hook(handlers,typeof(PlayerVitals),"LocalHit","PlayerHit",null));
                plan.Add(Hook(handlers,typeof(Server),"HitCreature","ServerCreatureHit",null));
                plan.Add(Hook(handlers,typeof(Server),"HitPlayer","ServerPlayerHit",null));
                plan.Add(Hook(handlers,typeof(ExplosionManager),"ServerExplode","BeforeExplosion",null,"EndExplosion"));
                plan.Add(Hook(handlers,typeof(ExplosionManager),"GetDamageOnCreature",null,"ExplosionDamage"));
            }
            else if (group == 4)
            {
                plan.Add(Hook(handlers,typeof(ProjectileManager),"AddProjectile","BeforeSingle","AfterLaunch"));
                plan.Add(Hook(handlers,typeof(ProjectileManager),"AddProjectiles","BeforeMultiple","AfterLaunch"));
                plan.Add(Hook(handlers,typeof(ProjectileManager),"UpdateProjectileScan","TrackProjectile",null));
            }
            else throw new ArgumentOutOfRangeException("group");
            foreach(var p in plan) { Validate(p.Original,p.Prefix); Validate(p.Original,p.Postfix); Validate(p.Original,p.Finalizer); }
            return plan;
        }
        public static void Validate(MethodInfo original, MethodInfo handler)
        {
            if(handler == null)return;
            foreach(var p in handler.GetParameters())
            {
                Type type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                if(p.Name == "__instance") { if(original.IsStatic || !type.IsAssignableFrom(original.DeclaringType))throw new MissingMethodException(handler.Name+".__instance"); continue; }
                if(p.Name == "__state")continue;
                if(p.Name == "__exception") {if(type != typeof(Exception))throw new MissingMethodException(handler.Name+".__exception");continue;}
                if(p.Name == "__result") {if(type != original.ReturnType)throw new MissingMethodException(handler.Name+".__result");continue;}
                ParameterInfo found = null;
                foreach(var native in original.GetParameters())if(native.Name == p.Name) {found = native;break;}
                if(found == null || type != (found.ParameterType.IsByRef ? found.ParameterType.GetElementType() : found.ParameterType))
                    throw new MissingMethodException(handler.Name+"."+p.Name);
            }
        }
        private static bool Has(MethodInfo original, MethodInfo handler)
        {
            if(handler == null)return true;
            var info = Harmony.GetPatchInfo(original);if(info == null)return false;
            foreach(var p in info.Prefixes)if(p.PatchMethod == handler)return true;
            foreach(var p in info.Postfixes)if(p.PatchMethod == handler)return true;
            foreach(var p in info.Finalizers)if(p.PatchMethod == handler)return true;
            return false;
        }
        public static bool Complete(List<GameplayHook> plan)
        {foreach(var p in plan)if(!Has(p.Original,p.Prefix)||!Has(p.Original,p.Postfix)||!Has(p.Original,p.Finalizer))return false;return true;}
        public static void Install(Harmony harmony, List<GameplayHook> plan)
        {
            var added = new List<GameplayHook>();
            try
            {
                foreach(var p in plan)
                {
                    var change = new GameplayHook {Original=p.Original,Prefix=Has(p.Original,p.Prefix)?null:p.Prefix,
                        Postfix=Has(p.Original,p.Postfix)?null:p.Postfix,Finalizer=Has(p.Original,p.Finalizer)?null:p.Finalizer};
                    if(change.Prefix == null && change.Postfix == null && change.Finalizer == null)continue;
                    added.Add(change);
                    harmony.Patch(change.Original,change.Prefix==null?null:new HarmonyMethod(change.Prefix),
                        change.Postfix==null?null:new HarmonyMethod(change.Postfix),null,change.Finalizer==null?null:new HarmonyMethod(change.Finalizer));
                }
                if(!Complete(plan))throw new InvalidOperationException("新增功能钩子未完整安装");
            }
            catch(Exception error)
            {
                var failures = new List<Exception> {error};
                for(int i=added.Count-1;i>=0;i--)
                {
                    var p=added[i];
                    foreach(var method in new[]{p.Prefix,p.Postfix,p.Finalizer})if(method!=null)
                        try {harmony.Unpatch(p.Original,method);} catch(Exception cleanup) {failures.Add(cleanup);}
                }
                if(failures.Count>1)throw new AggregateException("功能安装失败且部分钩子回退失败",failures);
                throw;
            }
        }
    }
}
