# harness-ac - AC Dreamport data-core harness (dotnet 8, C# 9)
Compiles `Assets/#User/SomniumAssetto/Data/**` (Unity-free) and probes the real install (found via AcInstall, or `--root <dir>`).
Build: `dotnet build -c Release -v q`  Run: `dotnet run -c Release --no-build -- <cmd>`
- `content` catalog summary | `kn5 <path> [tree|mats|tex]` | `acd <car> [entry]` | `ksanim <file|dir> [node]` | `knh <car>`
- `ai <track> [layout]` markers + surfaces + fast/pit lane + grid-vs-brute nearest check | `fsb <car|common|path>` | `fev <car|common|path> [filter]`
- `dds <kn5> <texture> <out.png>` decode one texture | `chunk <bank> <ID>` hex dump of FEV chunks | `hint <dir>` | `selftest` math/parsers
- `all [--decode]` every owned car kn5 (+LODs, collider) + textures + data.acd + bank, every ksanim/knh/driver kn5/ui json, every owned layout's markers + ai + full track kn5 mesh read
