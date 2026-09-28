# RIDElance SPV (aplicația desktop)

Aplicație Windows mică (WPF, .NET 10) care folosește stickul cu certificatul calificat al
împuternicitului ca să citească SPV-ul (SPVWS2) și trimite mesajele în RIDElance. Serverul nu poate
intra singur în SPV: SPVWS2 cere certificatul la fiecare conexiune, iar cheia privată nu iese de pe stick.

Aplicația doar transportă. Tot restul (asocierea după CIF, recipisele legate de declarații,
cererile, excepțiile) se face pe server și în aplicația web.

## Cum lucrează

- La pornire, la ieșirea din sleep și apoi la intervalul ales (30 min / 1 oră / 3 ore) face o trimitere:
  1. cere serverului de câte zile să citească (de la ultima trimitere reușită + 2 zile, maxim 60) și
     ce cereri așteaptă;
  2. `listaMesaje` la SPV, doar pentru CUI-urile clienților RIDElance;
  3. descarcă și trimite doar mesajele pe care serverul nu le are;
  4. trimite cererile din coadă (`cerere`) și raportează `id_solicitare`.
- Nu păstrează nimic local. Un PC închis în timpul trimiterii nu pierde și nu dublează nimic:
  serverul ignoră ce are deja și eliberează singur o trimitere rămasă deschisă (10 minute).
- PIN-ul stickului îl cere driverul stickului; aplicația nu îl salvează. Cheia RIDElance e criptată
  cu DPAPI în `%AppData%\RIDElance SPV\settings.json`.

## Folosire

1. În RIDElance (admin) → fișa unui client → tabul **ANAF** → **Aplicația SPV** → **Cheie nouă**.
2. În aplicație: lipești cheia → **Conectează** → **Alege** certificatul de pe stick.

## Build

```
dotnet build RidelanceSpv.slnx
dotnet test RidelanceSpv.Tests
dotnet publish RidelanceSpv -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

`publish/RIDElance SPV.exe` rulează fără .NET instalat.

Testul de contract cu serverul (`ServerContractTests`) rulează doar cu `RIDELANCE_SPV_SERVER` și
`RIDELANCE_SPV_KEY` setate (un backend de test cu o cheie SPV și un PFA cu CUI 12345674).
