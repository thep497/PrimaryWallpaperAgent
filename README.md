# PrimaryWallpaperAgent

เป้าหมายสุดท้าย: แสดงภาพ wallpaper บน Windows Primary Display เพียงจอเดียว และให้จออื่นเป็นสีดำ โดยปรับเมื่อเสียบ/ถอดจอหรือสลับ primary ไม่ผูกกับยี่ห้อหรือหมายเลขจอ

## สถานะปัจจุบัน: Probe + one-shot trial + bounded Console Guard

คำสั่ง Probe เดิมยังอ่านอย่างเดียว [Phase 2 one-shot trial](PHASE2-TRIAL.md) ตั้งภาพแยกจอสำเร็จแต่ภาพถูกตั้งกลับเมื่อสลับ Virtual Desktop จึงเพิ่ม [Console Guard แบบจำกัดเวลาทดลอง](CONSOLE-GUARD-TEST.md) ให้รันเองได้ก่อนเพิ่ม tray/autostart

ขั้นปัจจุบันกำลังวัด [event timing แบบอ่านอย่างเดียว](EVENT-PROBE-TEST.md) เพื่อเลือก trigger ก่อนแก้ Guard รุ่นถัดไป

- C# / .NET 10 (`net10.0-windows`), SDK 10.0.402, ไม่มี NuGet package ภายนอก
- อ่านจอที่ใช้งานใน Windows session ปัจจุบันผ่าน `EnumDisplayMonitors` และ `GetMonitorInfoW`
- ระบุ primary จาก `MONITORINFOF_PRIMARY`; ไม่ถือว่า DISPLAY1 เป็น primary เสมอ
- อ่าน monitor interface path ด้วย `EnumDisplayDevicesW` + `EDD_GET_DEVICE_INTERFACE_NAME`
- จับคู่กับ `IDesktopWallpaper.GetMonitorDevicePathAt` ด้วย device path แบบไม่แยกตัวพิมพ์ใหญ่/เล็ก
- อ่าน wallpaper path, monitor bounds, background color, position และ slideshow status
- แยกรายการจอ detached/cached เมื่อ `GetMonitorRECT` คืน `S_FALSE`
- ถ้าได้ error อื่นจะคง UNKNOWN พร้อม HRESULT; หาก ID ว่างจะไม่เรียก per-monitor getter และให้ HRESULT เป็น null
- ตรวจ topology ซ้ำท้าย snapshot; ถ้ามีการเปลี่ยนระหว่างอ่านจะให้ตรวจใหม่
- Probe รันหนึ่งครั้งแล้วจบ ไม่มี watcher, tray, autostart หรือการเขียน registry โดยตรง
- โหมด Probe ไม่อ่านเนื้อหาไฟล์ภาพหรือเปลี่ยน wallpaper; คำสั่ง Phase 2 อ่านภาพเฉพาะ source ที่ระบุและบันทึก state ตามเอกสาร Phase 2

Native COM interface มี declaration ของ setter เพื่อรักษาลำดับ vtable ตาม Windows SDK โค้ด Probe เรียกเฉพาะ getter; โค้ด trial ที่ต้องเรียก explicit ใช้ SetWallpaper โดยระบุ monitor ID เสมอ

## Build และรันจาก VS Code Terminal (PowerShell)

```powershell
Set-Location -LiteralPath 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent'
dotnet build --configuration Debug
.\bin\Debug\net10.0-windows\PrimaryWallpaperAgent.exe
```

ควรเรียก `.exe` โดยตรงเพื่อใช้ DPI manifest ของโปรแกรม (PerMonitorV2) และรันใน session ของผู้ใช้ที่ล็อกอินอยู่ ไม่ต้อง Run as administrator

คำสั่ง JSON ใช้ข้อมูลชุดเดียวกับ console โดยเขียนไฟล์เฉพาะเมื่อสั่ง redirect:

```powershell
New-Item -ItemType Directory -Path '.\snapshots' -Force | Out-Null
.\bin\Debug\net10.0-windows\PrimaryWallpaperAgent.exe --json > '.\snapshots\baseline.json'
$LASTEXITCODE
```

Exit code: `0` อ่านและจับคู่ครบ, `1` error ที่ทำต่อไม่ได้, `2` มีรายการกำกวม/อ่านไม่ครบ/argument ไม่ถูกต้อง

## วิธีอ่านผล

- `PRIMARY` คือ primary ตาม Windows ณ เวลาที่อ่าน
- `ExactDevicePath` คือจับคู่ด้วย ID โดยตรง; bounds ใช้ตรวจความสอดคล้องเพิ่มเติมเท่านั้น
- รายการ `DETACHED/CACHED` อาจเป็นจอที่เคยต่อ ไม่ใช่จำนวนจอที่ใช้งานปัจจุบัน
- wallpaper path ว่างหมายถึงไม่มีภาพ/solid color หรืออ่านผิดพลาด (ดู HRESULT) ไม่ใช่หลักฐานว่าจอเป็นสีดำ
- `BackgroundRgb` เป็นค่า global ของ API ไม่ใช่ผลวัดพิกเซลแต่ละจอ
- ภาพสีดำก็อาจเป็นไฟล์ภาพที่มี path ตามปกติได้
- ทดสอบ extended desktop 1–3+ จอตามจำนวนที่มีจริง; mirror/duplicate layout ที่กำกวมต้องตรวจแยก
- หากเปลี่ยน primary/เสียบถอดระหว่าง capture ให้รอ Windows จัดจอเสร็จแล้วรันใหม่

## Gate ก่อนเพิ่ม watcher/tray/autostart

ยังไม่สรุปว่า API สะท้อน wallpaper ของแต่ละ Virtual Desktop ได้ถูกต้อง ต้องทดลองบน Windows เครื่องนี้ก่อน:

1. ตรวจ baseline ใน desktop ปัจจุบัน ให้ผู้ใช้ยืนยันจำนวนจอ/primary/ภาพที่เห็นเทียบผล Probe
2. สลับไป Virtual Desktop ที่มีอยู่แล้วและรัน Probe โดยยังไม่เปลี่ยนภาพ ถ้าไม่มี desktop อื่น ให้ตกลงการสร้างชั่วคราวกับผู้ใช้ก่อน
3. เปรียบเทียบผลและภาพที่เห็นจริง หาก desktop เหล่านั้นใช้ภาพเดียวกันอยู่ การทดลองนี้ยังไม่พิสูจน์กรณี wallpaper ต่างกัน
4. การตั้งภาพต่างกันเพื่อทดลองต้องให้ผู้ใช้อธิบาย/อนุมัติแยก เพราะ Phase 1 ห้ามโปรแกรมแก้ wallpaper
5. ทดสอบแต่ละ extended topology: notebook เดี่ยว, ต่อจอเพิ่ม, สลับ primary แล้วถอด/เสียบใหม่ โดยผู้ใช้ทำเมื่อพร้อมและรัน Probe ซ้ำ
6. บันทึกข้อจำกัดจริง ก่อนออกแบบวิธีรักษาภาพต้นฉบับและ apply policy แล้วค่อยเพิ่ม watcher/tray/autostart

ไม่มีการอ้างว่า `IDesktopWallpaper` มี public API สำหรับ enumerate หรือเลือก Virtual Desktop เฉพาะตัว

ผลตรวจจากเครื่องจริงอยู่ใน [PHASE1-VALIDATION.md](PHASE1-VALIDATION.md) ผู้ใช้ยืนยันผลบนจอแล้ว ขณะนี้อยู่ระหว่าง [ทดสอบ Virtual Desktops](VIRTUAL-DESKTOP-TEST.md) ด้วยสคริปต์ `Capture-DesktopSnapshot.ps1`

## แหล่งอ้างอิง Microsoft

- [IDesktopWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper)
- [GetMonitorDevicePathAt: รวม detached monitor ได้](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-getmonitordevicepathat)
- [GetMonitorRECT และ S_FALSE](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-getmonitorrect)
- [GetWallpaper และ empty path](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-getwallpaper)
- [EnumDisplayDevicesW และ EDD_GET_DEVICE_INTERFACE_NAME](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw)
- [GetMonitorInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmonitorinfow)
- [Windows SDK COM declaration](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/ShObjIdl_core.h)
