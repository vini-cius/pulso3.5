/* SPDX-License-Identifier: GPL-3.0-or-later
 * Copyright (C) 2026 Pulso 3.5 contributors
 * See LICENSE for the full license text.
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Controlador35 {
    public sealed class GamerTelemetry {
        public bool Active,SourceDetected;
        public int ProcessId;
        public string GameName="",Source="",Status="";
        public double? Fps,FrameTimeMs,AverageFps,OnePercentLowFps;
        public DateTime At;
        public static GamerTelemetry Inactive(string status,bool sourceDetected) {
            return new GamerTelemetry { Status=status,SourceDetected=sourceDetected,At=DateTime.Now };
        }
        public static GamerTelemetry Disabled() { return Inactive("Modo gamer desativado",false); }
    }

    public static class GamerModePreferences {
        public static bool LoadEnabled() {
            try { return !String.Equals(File.ReadAllText(Paths.UserFile("modo-gamer.txt")).Trim(),"0",StringComparison.Ordinal); }
            catch { return true; }
        }
        public static void SaveEnabled(bool enabled) {
            File.WriteAllText(Paths.UserFile("modo-gamer.txt"),enabled?"1":"0");
        }
    }

    public static class GamerTelemetryParser {
        const uint RtssSignature=0x52545353, RtssAsciiSignature=0x53535452;
        const uint MahmSignature=0x4D41484D, MahmAsciiSignature=0x4D48414D;
        const int RtssFrameTimeOffset=280, RtssTime0Offset=268, RtssTime1Offset=272, RtssFramesOffset=276;
        const int MahmEntryDataOffset=1300, MahmEntryGpuOffset=1316, MahmEntrySourceIdOffset=1320, MahmEntryMinimumSize=1324;

        static uint U32(byte[] data,int offset) { return BitConverter.ToUInt32(data,offset); }
        static float F32(byte[] data,int offset) { return BitConverter.ToSingle(data,offset); }
        static bool Fits(byte[] data,uint offset,ulong length) {
            return data!=null&&offset<=(uint)data.Length&&length<=(ulong)data.Length-offset;
        }
        static bool IsSignature(uint value,uint expected,uint ascii) { return value==expected||value==ascii; }
        static bool ValidRate(double value) { return value>=0.1&&value<=2000&&!Double.IsNaN(value)&&!Double.IsInfinity(value); }
        static double? Positive(double value) { return ValidRate(value)?(double?)value:null; }

        public static GamerTelemetry ParseRtss(byte[] data,uint foregroundProcessId,string processName) {
            if(!Fits(data,0,20))return GamerTelemetry.Inactive("Memória do RTSS indisponível",false);
            uint signature=U32(data,0),version=U32(data,4);
            if(!IsSignature(signature,RtssSignature,RtssAsciiSignature)||version<0x00020000)
                return GamerTelemetry.Inactive("Memória do RTSS inválida ou incompatível",false);
            if((version>=0x0002000E)&&Fits(data,36,4)&&(U32(data,36)&1)!=0)
                return GamerTelemetry.Inactive("RTSS atualizando os dados",true);
            uint entrySize=U32(data,8),arrayOffset=U32(data,12),entryCount=U32(data,16);
            if(entrySize<284||entrySize>65536||entryCount>1024||arrayOffset<20||
                !Fits(data,arrayOffset,(ulong)entrySize*entryCount))
                return GamerTelemetry.Inactive("Tabela de jogos do RTSS inválida",true);

            if(foregroundProcessId==0)return GamerTelemetry.Inactive("RTSS ativo — aguardando um jogo em foco",true);
            for(uint index=0;index<entryCount;index++) {
                uint entryOffset=arrayOffset+index*entrySize;
                if(U32(data,(int)entryOffset)!=foregroundProcessId)continue;
                uint frameTimeUs=U32(data,(int)entryOffset+RtssFrameTimeOffset);
                double? fps=null,frameMs=null;
                if(frameTimeUs>0) {
                    frameMs=frameTimeUs/1000.0;
                    fps=Positive(1000000.0/frameTimeUs);
                }
                if(!fps.HasValue) {
                    uint time0=U32(data,(int)entryOffset+RtssTime0Offset);
                    uint time1=U32(data,(int)entryOffset+RtssTime1Offset);
                    uint frames=U32(data,(int)entryOffset+RtssFramesOffset);
                    uint elapsed=unchecked(time1-time0);
                    if(time0!=0&&elapsed>0&&frames>0) {
                        fps=Positive(1000.0*frames/elapsed);
                        if(fps.HasValue)frameMs=1000.0/fps.Value;
                    }
                }
                return new GamerTelemetry {
                    Active=true,SourceDetected=true,ProcessId=(int)foregroundProcessId,
                    GameName=String.IsNullOrWhiteSpace(processName)?"Jogo em foco":processName,
                    Source="RTSS",Status="Jogo em foco",Fps=fps,FrameTimeMs=frameMs,At=DateTime.Now
                };
            }
            return GamerTelemetry.Inactive("RTSS ativo — aguardando um jogo monitorado",true);
        }

        public static GamerTelemetry ParseAfterburner(byte[] data,uint foregroundProcessId,string processName) {
            if(!Fits(data,0,20))return GamerTelemetry.Inactive("Memória do Afterburner indisponível",false);
            uint signature=U32(data,0),version=U32(data,4);
            if(!IsSignature(signature,MahmSignature,MahmAsciiSignature)||version<0x00020000)
                return GamerTelemetry.Inactive("Memória do Afterburner inválida ou incompatível",false);
            uint headerSize=U32(data,8),entryCount=U32(data,12),entrySize=U32(data,16);
            if(headerSize<20||entrySize<MahmEntryMinimumSize||entrySize>65536||entryCount>4096||
                !Fits(data,headerSize,(ulong)entrySize*entryCount))
                return GamerTelemetry.Inactive("Tabela de sensores do Afterburner inválida",true);

            double? fps=null,frameMs=null,average=null,onePercentLow=null;
            for(uint index=0;index<entryCount;index++) {
                uint entryOffset=headerSize+index*entrySize;
                uint sourceId=U32(data,(int)entryOffset+MahmEntrySourceIdOffset);
                float raw=F32(data,(int)entryOffset+MahmEntryDataOffset);
                if(float.IsNaN(raw)||float.IsInfinity(raw)||Math.Abs(raw)>1000000)continue;
                uint gpu=U32(data,(int)entryOffset+MahmEntryGpuOffset);
                if(gpu!=0xFFFFFFFF)continue;
                if(sourceId==0x50&&raw>0)fps=Positive(raw);
                else if(sourceId==0x51&&raw>0&&raw<10000)frameMs=raw>1000?raw/1000.0:raw;
                else if(sourceId==0x53&&raw>0)average=Positive(raw);
                else if(sourceId==0x55&&raw>0)onePercentLow=Positive(raw);
            }
            if(!fps.HasValue&&frameMs.HasValue)fps=Positive(1000.0/frameMs.Value);
            bool active=foregroundProcessId!=0&&(fps.HasValue||frameMs.HasValue);
            if(!active)return new GamerTelemetry {
                SourceDetected=true,ProcessId=(int)foregroundProcessId,
                GameName=String.IsNullOrWhiteSpace(processName)?"Jogo em foco":processName,
                Source="MSI Afterburner",Status="Afterburner ativo — aguardando FPS do jogo",
                AverageFps=average,OnePercentLowFps=onePercentLow,At=DateTime.Now
            };
            return new GamerTelemetry {
                Active=true,SourceDetected=true,ProcessId=(int)foregroundProcessId,
                GameName=String.IsNullOrWhiteSpace(processName)?"Jogo em foco":processName,
                Source="MSI Afterburner",Status="Jogo em foco",Fps=fps,FrameTimeMs=frameMs,
                AverageFps=average,OnePercentLowFps=onePercentLow,At=DateTime.Now
            };
        }
    }

    public sealed class GamerModeReader {
        const uint FileMapRead=0x0004;
        const int MaxMapBytes=4*1024*1024;
        [StructLayout(LayoutKind.Sequential)]
        struct MemoryBasicInformation {
            public IntPtr BaseAddress,AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public UIntPtr RegionSize;
            public uint State,Protect,Type;
        }
        [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
        static extern IntPtr OpenFileMapping(uint desiredAccess,bool inheritHandle,string name);
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern IntPtr MapViewOfFile(IntPtr mapping,uint desiredAccess,uint offsetHigh,uint offsetLow,UIntPtr bytesToMap);
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern UIntPtr VirtualQuery(IntPtr address,out MemoryBasicInformation information,UIntPtr length);
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern bool UnmapViewOfFile(IntPtr address);
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);

        static byte[] ReadSharedMemory(string name,bool rtss) {
            IntPtr mapping=OpenFileMapping(FileMapRead,false,name);
            if(mapping==IntPtr.Zero)return null;
            IntPtr view=IntPtr.Zero;
            try {
                view=MapViewOfFile(mapping,FileMapRead,0,0,UIntPtr.Zero);
                if(view==IntPtr.Zero)return null;
                MemoryBasicInformation info;
                UIntPtr queried=VirtualQuery(view,out info,(UIntPtr)Marshal.SizeOf(typeof(MemoryBasicInformation)));
                if(queried==UIntPtr.Zero)return null;
                ulong regionBytes=info.RegionSize.ToUInt64();
                if(regionBytes<20||regionBytes>MaxMapBytes)return null;
                int length=(int)regionBytes;
                for(int attempt=0;attempt<3;attempt++) {
                    int busyBefore=0;
                    if(rtss&&length>=40&&unchecked((uint)Marshal.ReadInt32(view,4))>=0x0002000E) {
                        busyBefore=Marshal.ReadInt32(view,36);
                        if((busyBefore&1)!=0){Thread.Yield();continue;}
                    }
                    var copy=new byte[length];Marshal.Copy(view,copy,0,length);
                    if(rtss&&length>=40&&unchecked((uint)Marshal.ReadInt32(view,4))>=0x0002000E) {
                        int busyAfter=Marshal.ReadInt32(view,36);
                        if((busyAfter&1)!=0||busyBefore!=busyAfter){Thread.Yield();continue;}
                    }
                    return copy;
                }
                return null;
            } catch(AccessViolationException) { return null; }
              catch(ArgumentException) { return null; }
            finally {
                if(view!=IntPtr.Zero)UnmapViewOfFile(view);
                CloseHandle(mapping);
            }
        }
        static uint ForegroundProcessId() {
            IntPtr window=GetForegroundWindow();uint processId;
            if(window==IntPtr.Zero)return 0;
            GetWindowThreadProcessId(window,out processId);return processId;
        }
        static string ProcessName(uint processId) {
            if(processId==0)return "";
            try { using(Process process=Process.GetProcessById((int)processId))return process.ProcessName; }
            catch(ArgumentException) { return ""; }
            catch(InvalidOperationException) { return ""; }
            catch(System.ComponentModel.Win32Exception) { return ""; }
        }
        public GamerTelemetry ReadForeground() {
            uint processId=ForegroundProcessId();string processName=ProcessName(processId);
            byte[] rtss=ReadSharedMemory("RTSSSharedMemoryV2",true);
            GamerTelemetry fromRtss=rtss==null?GamerTelemetry.Inactive("RTSS não está em execução",false):
                GamerTelemetryParser.ParseRtss(rtss,processId,processName);
            byte[] afterburner=ReadSharedMemory("MAHMSharedMemory",false);
            GamerTelemetry fromAfterburner=afterburner==null?GamerTelemetry.Inactive("MSI Afterburner não está em execução",false):
                GamerTelemetryParser.ParseAfterburner(afterburner,processId,processName);
            if(fromRtss.Active) {
                if(fromAfterburner.SourceDetected) {
                    if(!fromRtss.Fps.HasValue)fromRtss.Fps=fromAfterburner.Fps;
                    if(!fromRtss.FrameTimeMs.HasValue)fromRtss.FrameTimeMs=fromAfterburner.FrameTimeMs;
                    fromRtss.AverageFps=fromAfterburner.AverageFps;
                    fromRtss.OnePercentLowFps=fromAfterburner.OnePercentLowFps;
                    if(fromAfterburner.AverageFps.HasValue||fromAfterburner.OnePercentLowFps.HasValue)
                        fromRtss.Source="RTSS + MSI Afterburner";
                }
                return fromRtss;
            }
            if(fromAfterburner.Active)return fromAfterburner;
            if(fromRtss.SourceDetected)return fromRtss;
            if(fromAfterburner.SourceDetected)return fromAfterburner;
            return GamerTelemetry.Inactive("Aguardando RTSS ou MSI Afterburner",false);
        }
    }
}
