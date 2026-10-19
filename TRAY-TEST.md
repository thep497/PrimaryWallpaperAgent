# System Tray v0.1.0 — เปิดเอง

ใช้ policy เดิมที่ผ่าน primary switch, Virtual Desktops และถอด/เสียบ 3 -> 1 -> 3 แล้ว โดยเปลี่ยนตัวรันเป็น WinForms `ApplicationContext` + `NotifyIcon` ไม่มีหน้าต่าง Console ค้างไว้ และยังไม่ตั้ง autostart

## เปิดและใช้งาน

```powershell
& 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent\Start-WallpaperTray.ps1'
```

ไอคอนโปรแกรมอาจอยู่ใต้ลูกศร ^ ข้างนาฬิกา คลิกขวาจะมีสถานะ, พัก/ทำงานต่อ, ตรวจภาพอีกครั้ง, เปิดบันทึก และออก (คงภาพปัจจุบัน)

1. รอประมาณ 2 วินาที ตรวจ primary มีภาพปกติและจออื่นดำ
2. คลิกขวาเลือกพัก ตรวจสถานะเป็นพักอยู่ แล้วเลือกทำงานต่อ
3. สลับ Virtual Desktop ตามปกติ ตรวจว่าภาพยังถูกต้อง
4. เลือกออก ตรวจว่าไอคอนหาย จากนั้นเปิดใหม่ด้วยสคริปต์เดิมได้

พักหรือออกหยุดการตรวจและแก้ไข ภาพที่ตั้งครั้งล่าสุดยังคงอยู่ ไม่ใช่คำสั่งคืนภาพเดิม หากต้องใช้สคริปต์ trial/restore ให้ปิด tray ก่อน เพราะใช้ mutex ป้องกันตัวเขียนซ้อนกัน

## พฤติกรรมและข้อจำกัด

- ตรวจทุก 1 วินาที ต้องมี fingerprint เหมือนกันสองรอบก่อนปรับ อาจมีภาพเดิมแวบขึ้นระหว่างรอ ไม่รับประกันการซ่อนภาพทันที
- สร้างแผนจาก active IDs/primary ปัจจุบันทุกครั้ง ไม่เก็บหมายเลขจอหรือ session เก่าเป็น policy
- ตั้งเฉพาะ per-monitor wallpaper; ไม่เปลี่ยน position/background/slideshow และไม่ใช้ private Virtual Desktop API
- ใช้ภาพสำเนาใน package และตรวจ SHA-256 ก่อนเริ่ม/ทำงานต่อ/ปรับภาพ ไม่ต้องเปิดอ่าน OneDrive ต้นฉบับอีก
- ต้องเป็น Picture / Fill เช่นเดิม ถ้าเจอภาพที่ไม่รู้จักหรือเปลี่ยนโหมดจะพักให้ตรวจ log แทนการทับสิ่งที่ผู้ใช้เลือก เมนูทำงานต่อไม่ได้ยืนยันภาพใหม่โดยอัตโนมัติ
- Mapping ระหว่าง hotplug ผิดพลาดชั่วคราวให้รอได้ 10 รอบ; apply/readback/rollback ผิดพลาดให้พักทันที
- ถ้าปรับซ้ำ 8 ครั้งภายใน 60 วินาทีจะพักเพื่อหลีกเลี่ยงการแย่งตั้งกับโปรแกรมอื่น
- ก่อนเขียนเก็บ before snapshot และหลังสำเร็จเก็บ after snapshot ใน `artifacts/PrimaryWallpaperAgent-v0.1.0/runtime/<run>/` พร้อม agent.log ไม่มีภาพหรือข้อมูลเครื่องใน Git
- Framework-dependent ใช้ .NET Desktop runtime ที่ติดตั้งไว้แล้ว ไม่มี NuGet ภายนอก
- Log/snapshots เก็บเฉพาะการเริ่ม/พัก/แก้ไข/ผิดพลาด/ออก ไม่บันทึก snapshot ทุกวินาที ข้อมูลเก่าไม่ลบอัตโนมัติในรุ่นนี้
- Projector, 4+ จอจริง, restart Explorer และ reboot/logon ยังต้องตรวจเพิ่ม; ไม่ถือว่าการทดสอบจำลองหรือ hotplug ชุดเดิมครอบคลุมทุกอุปกรณ์

## Build และการตรวจ

ผลตรวจ 19 ตุลาคม 2569: Release publish ผ่าน, policy tests แบบจำลอง 1/3/5 จอและ rollback ผ่าน; read-only tray smoke test บน Windows จริงทำงาน 5.09 วินาที อ่าน 4 รอบ, repairs=0, exit=0 ยังรอผู้ใช้ตรวจเมนูจริง

`Publish-Tray.ps1` ใช้ local plan เพื่อจัดสำเนาภาพ/paths ที่อนุมัติไว้ แล้วสร้าง package ใน artifacts ซึ่ง Git ignore อยู่ ต้องปิด tray ก่อน publish ซ้ำ

โหมด `PrimaryWallpaperAgent.Tray.exe --smoke-test` สร้าง tray/message loop อ่านจอโดยไม่เรียก setter และออกเองภายในประมาณ 5 วินาที ผลอยู่ใน runtime ของรอบนั้น การสร้างไอคอนสำเร็จยังต้องให้ผู้ใช้ตรวจเมนูจริง

อ้างอิง Microsoft: [NotifyIcon](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0), [ApplicationContext](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.applicationcontext?view=windowsdesktop-10.0)

WinForms ตั้ง PerMonitorV2 ผ่าน `ApplicationHighDpiMode` และ `ApplicationConfiguration.Initialize()` ตาม [WFO0003](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/compiler-messages/wfo0003); manifest ของ Console Probe เดิมคงแยกไว้
