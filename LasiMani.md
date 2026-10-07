# Serpentīna Hamiltona Cikls (VCSM)

🇬🇧 English: [README.md](README.md)

**VCSM** (Vector Chain Spatial Model) ir rīku kopa šaha zirdziņa gājienu
virknēm un no tām veidotiem **Hamiltona cikliem/ceļiem** — ar pielietojumu
prepress, tekstila un CNC/lāzera darbos, kur "serpentīna" tipa nepārtraukts
ceļš, kas apmeklē katru virsmas šūnu tieši vienu reizi, ir noderīgs reālai
fiziskai apstrādei (izšūšana, gravēšana, griešana u.tml.).

Ceļi tiek kodēti kā **ciparu virknes** (katrs cipars 1-8 = viens no 8
iespējamiem zirdziņa gājienu virzieniem), ļaujot kompakti glabāt un
pārveidot ļoti garus ceļus (simtiem tūkstošu gājienu).

## Satura rādītājs

- [Darbplūsma](#darbplūsma)
- [Rīki](#rīki)
  - [Python](#python-rīki)
  - [C#](#c-rīki)
  - [Tīmekļa skatītājs](#tīmekļa-skatītājs)
- [Failu formāti](#failu-formāti)
- [Prasības](#prasības)

## Darbplūsma

Tipiska pilna ceļa: no attēla/formas līdz gatavam apvienotam Hamiltona ciklam.

```
1. serpentinaHC3_6x6.exe 6 6
   → ģenerē 6×6 zirdziņa cikla blokus (cikls4abt6x6.hrz)

2. python cycles_to_gatavie_abt_6x6.py cikls4abt6x6.hrz gatavie_abt_6x6
   → pārveido par gatavu "plēsto" .abt katalogu (hub-satelīts griešana)

3. python png_sensors_to_ffseciba.py attēls.png FFseciba.txt
   (vai C# versija: PngSensorsToFFseciba.exe attēls.png FFseciba.txt)
   → attēls (taisnstūris+sensori UN/VAI tieva līnija/spirāle/kontūra)
     → rūtiņu tīkls → Hamiltona ceļš

4. python serpentina_hc_v12.py gatavie_abt_6x6 FFseciba.txt
   → saliek 6×6 blokus pēc FFseciba.txt shēmas → viens garš .hrz ceļš

5. HCeluSavienoshanaWinForms.exe
   → apvieno DIVUS atsevišķus ceļus vienā (lūzuma punkti, rotācija,
     transformācijas, automātiska savienojumu meklēšana)

6. mikroScope.html
   → gatavā ceļa apskate pārlūkā, SVG eksports koplietošanai
```

## Rīki

### Python rīki

| Fails | Apraksts |
|---|---|
| `cycles_to_gatavie_abt_6x6.py` | No savāktiem 6×6 zirdziņa cikliem izveido gatavu "plēsto" `.abt` katalogu vienā solī (hub-satelīts virziena griešana + rotācijas/spoguļojuma varianti). |
| `serpentina_hc_v12.py` | Saliek 6×6 (vai 16×16) blokus lielākā "palagā" pēc FFseciba shēmas. Atbalsta atvērtus un slēgtus ceļus, progresa ziņojumus meklēšanas laikā. |
| `diagnose_ffseciba.py` | Diagnostikas rīks — izsmeļoši pārbauda katru bloku pāreju FFseciba shēmā. |
| `png_sensors_to_ffseciba.py` | PNG attēls → FFseciba.txt. Atpazīst gan blīvu taisnstūri ar sensoru izgriezumiem, gan tievu līniju/spirāli/kontūru (ar iztieknošanu, spuru tīrīšanu, savienoto komponenšu meklēšanu). |

**Nepieciešamās bibliotēkas:** `numpy`, `Pillow` (PIL), `scikit-image`, `scipy`.

### C# rīki

| Fails | Apraksts |
|---|---|
| `HCeluSavienoshanaWinForms.cs` | Windows Forms rīks DIVU HC ceļu savienošanai: lūzuma punkti (ar peles klikšķi vai koordinātēm), 2. ceļa rotācija (ar automātisku ieliektu stūru/mazu ciklu tīrīšanu), F1-F9 transformācijas, automātiska savienojumu meklēšana, +/- soļa pogas ātrai nobīžu/centra regulēšanai. |
| `PngSensorsToFFseciba.cs` | C# ports no `png_sensors_to_ffseciba.py` — pašrakstīts Guo-Hall iztieknošanas algoritms un savienoto komponenšu marķēšana (aizstāj Python `scikit-image`/`scipy.ndimage`, kam nav tiešu C# ekvivalentu). |

**Nepieciešams:** .NET Framework (Visual Studio) ar `System.Drawing` atsauci
(konsoles lietotnei) vai Windows Forms (grafiskajam rīkam).

### Tīmekļa skatītājs

| Fails | Apraksts |
|---|---|
| `mikroScope.html` | Patstāvīgs HTML/JS rīks liela HC ceļa ("palaga") apskatei — tālummaiņa, krāsu maiņa pēc gājiena numura, lūzuma punkta meklēšana pēc koordinātēm, F1-F9 transformācijas, SVG eksports (ar iegultu peles ritenīša tuvināšanu/vilkšanu un dubultklikšķa detalizētu tuvinājumu). |

Atver tieši pārlūkā (dubultklikšķis) — nav vajadzīgs serveris.

## Failu formāti

- **`.hrz` / `.vrz` / `.txt`** — ceļa gājienu virkne kā ciparu teksts (1-8 katrs cipars).
- **`.bin`** — tas pats, bināri kompaktākā formā.
- **`FFseciba.txt`** — CSV tipa rūtiņu tīkls, kur katra šūna satur tās apmeklējuma kārtas numuru (vai tukša, ja šūna nav daļa no ceļa).
- **`.abt`** — 6×6 (vai 16×16) "plēsto" bloku katalogs ar manifestu, ko izmanto `serpentina_hc_v12.py` bloku saskaņošanai.

## Prasības

- **Python 3.x** ar `numpy`, `Pillow`, `scikit-image`, `scipy`
- **.NET Framework** (Visual Studio) C# rīkiem
- Jebkurš mūsdienu pārlūks `mikroScope.html` skatīšanai

---

*Projekts attīstīts pakāpeniski, risinot reālas problēmas ar lielu (6×6, 16×16, un lielāku) Hamiltona ciklu salikšanu, attēlu-uz-ceļu konversiju un ceļu vizuālu apskati/apvienošanu.*
