"""Run the compiled quantity fixture offline with the installed game's Mono."""
import ctypes
import math
import os
import sys
from pathlib import Path

managed, fixture, candidate = (Path(p).resolve(strict=True) for p in sys.argv[1:])
mono_root = managed.parent.parent / "MonoBleedingEdge"
os.chdir(fixture.parent)
mono = ctypes.CDLL(str(mono_root / "EmbedRuntime" / "mono-2.0-bdwgc.dll"))
ptr = ctypes.c_void_p
cp = ctypes.c_char_p
for name, result, parameters in (
    ("mono_set_dirs", None, [cp, cp]),
    ("mono_set_assemblies_path", None, [cp]),
    ("mono_jit_init_version", ptr, [cp, cp]),
    ("mono_add_internal_call", None, [cp, ptr]),
    ("mono_domain_assembly_open", ptr, [ptr, cp]),
    ("mono_jit_exec", ctypes.c_int, [ptr, ptr, ctypes.c_int, ctypes.POINTER(cp)]),
):
    function = getattr(mono, name)
    function.restype = result
    function.argtypes = parameters

mono.mono_set_dirs(os.fsencode(mono_root), os.fsencode(mono_root / "etc"))
mono.mono_set_assemblies_path(os.fsencode(managed))
domain = mono.mono_jit_init_version(b"SpawnQuantities", b"v4.0.30319")
if not domain:
    sys.exit("Mono initialization failed")


@ctypes.CFUNCTYPE(None, ctypes.POINTER(ctypes.c_float), ctypes.POINTER(ctypes.c_float))
def circular_rotation(vector, rotation):
    # The circular event plans only rotate around the Y axis. Supply that native
    # Unity operation so their actual managed instruction builders can run offline.
    if vector[0] != 0 or vector[2] != 0:
        os._exit(89)
    rotation[0], rotation[1], rotation[2], rotation[3] = 0, math.sin(vector[1]/2), 0, math.cos(vector[1]/2)


mono.mono_add_internal_call(b"UnityEngine.Quaternion::Internal_FromEulerRad_Injected", ctypes.cast(circular_rotation, ptr))

@ctypes.CFUNCTYPE(ctypes.c_float)
def reject_live_clock():
    # The invalid Boss command must return before reaching a live wave's clock.
    os._exit(90)


mono.mono_add_internal_call(b"UnityEngine.Time::get_realtimeSinceStartup", ctypes.cast(reject_live_clock, ptr))
assembly = mono.mono_domain_assembly_open(domain, os.fsencode(fixture))
if not assembly:
    sys.exit("Mono could not load the spawn quantity fixture")
argv = (cp * 2)(os.fsencode(fixture), os.fsencode(candidate))
sys.exit(mono.mono_jit_exec(domain, assembly, len(argv), argv))
