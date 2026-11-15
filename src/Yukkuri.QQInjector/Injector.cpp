#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include "MinHook.h"

// All V8 calls stay on QQ's JavaScript thread. The exported ABI is checked
// before installing the hook; no QQ version offsets or disk patches are used.
using Call = void* (*)(void*, void**, void*, void*, int, void*);
using GetIsolate = void* (*)();
using ScopeCtor = void (*)(void*, void*);
using ScopeDtor = void (*)(void*);
using NewString = void* (*)(void**, void*, const char*, int, int);
using Compile = void* (*)(void**, void*, void*, void*);
using Run = void* (*)(void*, void**, void*);

static Call originalCall;
static GetIsolate getIsolate;
static ScopeCtor scopeCtor;
static ScopeDtor scopeDtor;
static NewString newString;
static Compile compile;
static Run run;
static void* target;
static std::string source;
static volatile LONG claimed;
static thread_local bool inside;

static void* CallHook(void* self, void** result, void* context, void* receiver, int argc, void* argv)
{
    if (inside) return originalCall(self, result, context, receiver, argc, argv);
    inside = true;
    auto returned = originalCall(self, result, context, receiver, argc, argv);
    if (context && InterlockedCompareExchange(&claimed, 1, 0) == 0)
    {
        auto isolate = getIsolate();
        if (isolate)
        {
            alignas(16) unsigned char scope[64] = {};
            scopeCtor(scope, isolate);
            void* text = nullptr;
            void* script = nullptr;
            void* ignored = nullptr;
            newString(&text, isolate, source.c_str(), 0, static_cast<int>(source.size()));
            if (text) compile(&script, context, text, nullptr);
            if (script) run(script, &ignored, context);
            scopeDtor(scope);
        }
        MH_DisableHook(target);
    }
    inside = false;
    return returned;
}

extern "C" __declspec(dllexport) DWORD WINAPI YukkuriInitialize(const char* script)
{
    if (!script || !*script) return 1;
    if (target) return 2;
    auto module = GetModuleHandleW(L"QQNT.dll");
    if (!module) return 3;
    target = reinterpret_cast<void*>(GetProcAddress(module,
        "?Call@Function@v8@@QEAA?AV?$MaybeLocal@VValue@v8@@@2@V?$Local@VContext@v8@@@2@V?$Local@VValue@v8@@@2@HQEAV52@@Z"));
    getIsolate = reinterpret_cast<GetIsolate>(GetProcAddress(module, "?TryGetCurrent@Isolate@v8@@SAPEAV12@XZ"));
    scopeCtor = reinterpret_cast<ScopeCtor>(GetProcAddress(module, "??0HandleScope@v8@@QEAA@PEAVIsolate@1@@Z"));
    scopeDtor = reinterpret_cast<ScopeDtor>(GetProcAddress(module, "??1HandleScope@v8@@QEAA@XZ"));
    newString = reinterpret_cast<NewString>(GetProcAddress(module,
        "?NewFromUtf8@String@v8@@SA?AV?$MaybeLocal@VString@v8@@@2@PEAVIsolate@2@PEBDW4NewStringType@2@H@Z"));
    compile = reinterpret_cast<Compile>(GetProcAddress(module,
        "?Compile@Script@v8@@SA?AV?$MaybeLocal@VScript@v8@@@2@V?$Local@VContext@v8@@@2@V?$Local@VString@v8@@@2@PEAVScriptOrigin@2@@Z"));
    run = reinterpret_cast<Run>(GetProcAddress(module,
        "?Run@Script@v8@@QEAA?AV?$MaybeLocal@VValue@v8@@@2@V?$Local@VContext@v8@@@2@@Z"));
    if (!target || !getIsolate || !scopeCtor || !scopeDtor || !newString || !compile || !run)
    {
        target = nullptr;
        return 4;
    }
    source = script;
    if (MH_Initialize() != MH_OK) return 5;
    if (MH_CreateHook(target, reinterpret_cast<void*>(&CallHook),
        reinterpret_cast<void**>(&originalCall)) != MH_OK) return 6;
    if (MH_EnableHook(target) != MH_OK) return 7;
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
