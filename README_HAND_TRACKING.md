# Hand Tracking Setup

Deze applicatie gebruikt hand tracking via je webcam om de spotlight te besturen.

## Installatie

### Stap 1: Installeer Python
Als je Python nog niet hebt, download dan Python 3.8 of hoger van [python.org](https://www.python.org/downloads/)

### Stap 2: Installeer dependencies
Open een command prompt of terminal in de projectmap en voer uit:

```bash
pip install -r requirements.txt
```

Dit installeert:
- **opencv-python**: Voor camera toegang
- **mediapipe**: Voor hand detectie

## Gebruik

### Stap 1: Start de hand tracker
In een command prompt of terminal:

```bash
python hand_tracker.py
```

Het script zal:
1. Automatisch het MediaPipe hand model downloaden (eenmalig, ~20MB)
2. Je camera openen
3. Een preview venster tonen
4. Hand coördinaten via UDP naar de WPF applicatie sturen

### Stap 2: Start de WPF applicatie
- Open de solution in Visual Studio
- Druk op F5 of Start
- De applicatie ontvangt nu hand coördinaten van het Python script

### Stap 3: Test
- Beweeg je hand voor de camera
- De spotlight in de WPF app volgt je hand
- Verwijder je hand uit beeld ? spotlight verdwijnt

## Troubleshooting

### Camera wordt niet gevonden
- Sluit andere apps die de camera gebruiken (Teams, Zoom, Skype, Edge)
- Controleer Windows Camera privacy instellingen:
  - Settings ? Privacy & security ? Camera
  - Sta desktop apps toe om de camera te gebruiken

### "Port already in use" fout
- Zorg dat de WPF applicatie draait (die luistert op poort 5005)
- Of een andere instantie van hand_tracker.py draait

### Slechte tracking
- Zorg voor goede verlichting
- Houd je hand plat en open voor beste resultaten
- Zorg dat je hand goed zichtbaar is in de camera

## Debug opties

De WPF applicatie heeft ook muis besturing als fallback:
- Beweeg je muis over het venster ? spotlight volgt de muis
- Verlaat het venster ? spotlight verdwijnt

## Afsluiten

- Druk op **ESC** in het hand tracker preview venster
- Of sluit het command prompt venster
