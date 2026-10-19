# Config v0.2.0

เปิดจากเมนู Tray **ตั้งค่า…** หรือดับเบิลคลิกไอคอน มีภาพตัวอย่าง, เลือกไฟล์, ใช้ภาพบนจอหลักตอนนี้, เปิดพร้อม Windows, ดูบันทึก, บันทึกและใช้ และยกเลิก

## การทำงาน

- เปิด Config จะพักการปรับภาพอัตโนมัติไว้ชั่วคราว การเลือก/ดูตัวอย่าง/ยกเลิกไม่เรียก setter และไม่บันทึก config
- บันทึกและใช้เป็นคำสั่งให้ใช้ภาพที่เลือกบน primary ปัจจุบัน และดำทุกจอรอง รวมกรณีภาพก่อนหน้านั้นเป็นภาพอื่นที่ผู้ใช้เลือกผ่าน Windows
- อ่านเฉพาะไฟล์ภาพที่ผู้ใช้เลือกจาก picker หรือกดใช้ภาพบนจอหลักตอนนี้ ไม่ค้นโฟลเดอร์ภาพหรือ cache
- รองรับ JPG/PNG/BMP ไม่เกิน 32 MB / 40 ล้านพิกเซล เก็บสำเนาตาม SHA-256 ใน assets ไม่แก้ต้นฉบับและไม่ล็อกไฟล์ต้นฉบับค้างไว้
- บันทึก settings แบบเปลี่ยนไฟล์ด้วย File.Replace บน volume เดียวกัน; เก็บ bytes เดิมและแผนก่อนเขียนใน runtime; ถ้าตั้งภาพ/บันทึก/ตั้ง startup ไม่สำเร็จ จะพยายามคืนค่าที่เปลี่ยน และรายงานชัดเมื่อคืนไม่ครบ
- Startup เริ่มปิด ค่าในไฟล์ไม่ทำให้โปรแกรมลงทะเบียนเองตอนเปิดแอป มีการเขียนเฉพาะเมื่อกดบันทึกโดยเลือกค่าให้ต่างจากที่ลงทะเบียนอยู่
- Startup ใช้เพียง value `PrimaryWallpaperAgent` ที่ `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` สำหรับบัญชีปัจจุบัน ไม่ต้องยกระดับสิทธิ์ ไม่อ่านหรือแก้ startup รายการอื่น Windows/Task Manager อาจปิดกั้นการเริ่มโปรแกรมได้ ยังต้องทดสอบ logon จริงเมื่อผู้ใช้เลือกเปิด
- รูปแบบ Picture / Fill และ static-image policy เดิม ไม่เพิ่ม Spotlight, Slideshow หรือการเลือกภาพต่างกันในแต่ละ Virtual Desktop
- เมื่อพบภาพ/โหมดไม่รู้จัก โปรแกรมพักและเตือนหนึ่งครั้ง เมนูเลือกภาพและทำงานต่อจะเปิด Config ไม่เริ่มตรวจซ้ำจนพักวน การยกเลิกในสถานะนี้จะคงการพักอยู่
- การปิดหน้าตั้งค่าไม่ปิด Tray และการออกจาก Tray คงภาพที่ตั้งครั้งล่าสุดไว้
- Publish แยก artifacts/PrimaryWallpaperAgent-v0.2.0 จาก v0.1.0; publish ซ้ำไม่เขียนทับ settings ที่ผู้ใช้บันทึกแล้ว ต้องปิดโปรแกรมก่อน publish
- runtime journal และสำเนาภาพไม่อยู่ใน Git รุ่นนี้ยังไม่ลบประวัติ/ภาพสำเนาที่เลิกใช้โดยอัตโนมัติ

## ขั้นทดสอบของผู้ใช้

1. ใน Config เลือกภาพใหม่ ตรวจตัวอย่างก่อน โดยจอจริงต้องยังไม่เปลี่ยน
2. กดบันทึกและใช้ ตรวจว่าภาพใหม่อยู่เฉพาะ primary อีกทุกจอดำ จากนั้นสลับ Virtual Desktop
3. เปิด Config อีกครั้ง ตรวจว่าจำภาพไว้ และลองเลือกภาพอื่นแล้วกดยกเลิก ภาพที่ใช้อยู่ต้องคงเดิม
4. ถ้าต้องการรับภาพที่เลือกผ่าน Windows: เปลี่ยนผ่าน Windows รอแจ้งพบภาพใหม่ แล้วเปิด Config กดใช้ภาพบนจอหลักตอนนี้และบันทึกและใช้
5. ช่องเปิดพร้อม Windows ยังไม่ต้องติ๊กในรอบแรก ค่อยทดสอบ logon เป็นขั้นถัดไป

เปิดหน้า Config โดยตรงเมื่อแอปยังไม่ทำงาน:

```powershell
& 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent\Start-WallpaperTray.ps1' -Config
```

## การตรวจที่ทำก่อนส่งทดสอบ

- Release publish และชุดทดสอบเดิมของ Probe/Trial/Guard/Tray policy
- `--self-test-config`: ใช้ไฟล์ fixture ใน runtime และ fake desktop/startup; ทดสอบเลือกภาพ, primary-only, opt-in/out startup, ไม่แก้ต้นฉบับ, failed setter/settings/startup, failed rollback reporting, external changes, hash/path validation, invalid image, preview/cancel และอ่าน settings เดิมรวม UTF-8 BOM
- วาดเฉพาะหน้าต่าง Config ของโปรแกรมเป็น PNG เพื่อตรวจภาษาไทยและ layout ไม่จับภาพหน้าจอแอปอื่น
- `--smoke-test`: อ่าน native display จริง 4 รอบประมาณ 5 วินาที, repairs=0, exit=0 เปรียบเทียบ wallpaper/settings ก่อนและหลังตรงกัน
- ยังรอผู้ใช้ยืนยัน Save/Cancel/picker และภาพจริงของ v0.2.0; ทดสอบจำลองไม่ใช่ผล native apply ของรุ่นนี้

อ้างอิง Microsoft: [File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace), [Run/RunOnce keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys), [OpenFileDialog](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.openfiledialog)
