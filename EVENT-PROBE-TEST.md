# Read-only event timing probe

ขั้นนี้ผู้ใช้อนุญาตให้เก็บ event เพื่อวัดจังหวะ Virtual Desktop ก่อนแก้ Guard เพิ่ม ไม่มี setter ในเส้นทาง event-probe และไม่เปลี่ยน Guard เดิม

## รัน

```powershell
& 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent\Start-WallpaperEventProbe.ps1' -Seconds 120
```

เมื่อเห็น EVENT_PROBE_READY ให้สลับ desktop 1 -> 2 -> 1 โดยรอแต่ละ desktop ประมาณ 3 วินาที ถ้ามี desktop 3 อยู่แล้วอาจสลับไปด้วย ไม่ต้องสร้าง desktop เพิ่ม เปิด observer ทิ้งไว้ขณะสลับแล้วกลับมาหยุดด้วย Ctrl+C หรือรอครบเวลา

ระหว่างเก็บผลอย่ารัน Guard/apply/restore และอย่าตั้ง wallpaper ใหม่ เพราะจะปนกับเหตุการณ์จาก Windows ตัว observer ไม่แก้ภาพ ถ้าจอรองกลับมีภาพ ให้ปล่อยไว้เพื่อเก็บหลักฐาน

Observer ถือ mutex สำหรับกัน Guard/apply/restore ของโปรแกรมนี้ทำงานซ้อนระหว่างวัดผล ถ้ามี Guard รันอยู่จะไม่เริ่ม observer ให้หยุด Guard ก่อน ไม่ได้ขวาง Windows หรือโปรแกรมอื่นเปลี่ยนค่า

## สิ่งที่เก็บ

- WinEvent: foreground (0x0003), desktop switch (0x0020), cloaked/uncloaked (0x8017/0x8018)
- Hidden top-level window รับ WM_SETTINGCHANGE, WM_DISPLAYCHANGE และ WM_THEMECHANGED โดยไม่มีหน้าต่างแสดงหรือแย่ง focus
- ไม่อ่านชื่อ/เนื้อหาหน้าต่าง, lParam strings, keyboard input, registry หรือไฟล์ภาพ; ไม่มี DLL ภายนอก
- WinEvent เป็น out-of-context; callback enqueue เท่านั้น ใช้ message pump และรักษา delegate lifetime; ไม่มี COM/file I/O ใน callback
- ช่วง idle อ่าน wallpaper 1 วินาทีต่อครั้ง หลัง event อ่านถี่ทุก 100 ms เป็นเวลา 2 วินาทีเพื่อวัดช่วงเปลี่ยนค่า ทุก COM query รันบน STA เดิม
- callback มีขีดจำกัดคิว 10,000 records และรายงาน dropped events / callback errors

## หลักฐาน (ไม่เข้า Git)

ใต้ snapshots/event-probe-*:

- events.jsonl: timestamp เมื่อ callback มาถึง และ generation uptime/delivery age สำหรับ WinEvent
- samples.jsonl: เวลาเริ่ม/จบแต่ละ read, state hash และ event ล่าสุด
- states.jsonl: full snapshot ครั้งแรกและเมื่อ state hash เปลี่ยน
- summary.json: จำนวน event, sample, state transitions, dropped/error counters และเหตุผลจบ

ต้องเทียบการสลับที่ผู้ใช้ทำจริงกับ event/state timeline ไม่ถือว่า foreground/cloak/desktop-switch ทุก event คือ Virtual Desktop switch และไม่อ้างว่าเวลาที่ API เปลี่ยนคือเวลาที่ภาพ render เสร็จ

ถ้าเปลี่ยน desktop แล้ว wallpaper path เหมือนเดิม จะไม่มี wallpaper state transition ให้ใช้วัด latency แม้อาจมี event เกิดขึ้น จึงยังสรุป timing ของการตั้งภาพไม่ได้

## Native self-test

--self-test-events รัน 3 วินาที ลงทะเบียน hooks และ hidden window จริง จากนั้นส่ง private message เฉพาะ hidden window ของตัวเองหลัง GC ไม่ broadcast event ระบบหรือเปลี่ยน wallpaper ต้องเห็น LOCAL_SELF_TEST ก่อนถือว่าผ่าน ส่วน WinEvent delivery จากการสลับ desktop ต้องให้ผู้ใช้ทดลองจริง

ผล native self-test วันที่ 19 ตุลาคม 2569: build ผ่าน 0 warnings/errors, ลงทะเบียน 3 hooks, รับ LOCAL_SELF_TEST หลัง GC, อ่าน 16 samples ใน 3 วินาที, state transitions=0, dropped events=0 และไม่มี callback errors โหมด Probe เดิมยังอ่านได้ครบ active 3 จอ โดยมีข้อสังเกต API เดิม 7 รายการ ตรวจ call path แล้วไม่มี setter และไม่มีการแก้ไฟล์ WallpaperGuard.cs

หลักฐาน self-test ล่าสุดอยู่ใน snapshots/event-probe-20261019_084957_307-61a7573e; ยังรอ manual Virtual Desktop event test เพื่อเลือกสัญญาณและเวลา debounce

## อ้างอิง Microsoft

- [SetWinEventHook: message loop, out-of-context และ managed callback lifetime](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)
- [Event constants](https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants)
- [WM_SETTINGCHANGE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange)

ยังไม่ใช้ private COM notification หรือ registry schema ของ Virtual Desktops ขั้นนี้ทดสอบสัญญาณ Win32 ที่บันทึกได้ก่อน หากไม่เพียงพอจึงพิจารณาตัวรับ notification ที่เจาะจงขึ้นตาม Windows build
