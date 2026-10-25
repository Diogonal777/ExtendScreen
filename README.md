# ExtendScreen

[English](#english) | [Русский](#русский)

---

## English

Use your Android device as an extended display for your Windows PC, supporting both USB and Wi-Fi connections.

### Features

- Extended Display: Use your tablet/phone as a second monitor for your PC
- USB Connection: Low latency via ADB port forwarding
- Wi-Fi Connection: Wireless extension, no cable needed
- PC-to-PC: Extend to another Windows PC as a secondary display
- Keep Screen On: Prevents Android device from sleeping while connected
- Mouse Sync: Normal cursor displayed on the extended screen

### Project Structure

```
ExtendScreenAndroid/    # Android app - receives and displays video stream
ExtendScreenServer/     # Windows server - captures screen and streams
ExtendScreenClient/     # Windows client - acts as an extended display on another PC
```

### How It Works

1. The Windows server captures the screen of a specified region, encodes it as JPEG frames, and sends them via TCP
2. The Android app receives JPEG frames, decodes them with hardware decoder, and renders to a SurfaceView
3. The Android app captures touch/input events and sends them back to the Windows server for mouse injection
4. Communication is established via ADB port forwarding (USB) or LAN TCP (Wi-Fi)

### Usage

#### Prerequisites

- Windows 10/11 (x64)
- Android 8.0+ device
- ADB (Android Debug Bridge)
- USB debugging enabled (for USB connection)

#### USB Connection

1. Connect your Android device to the PC with a USB cable
2. Enable USB debugging
3. Launch the Windows app and select USB mode
4. Tap Connect on the Android app

#### Wi-Fi Connection

1. Ensure the PC and Android device are on the same LAN
2. Launch the Windows app and select Wi-Fi mode
3. Enter the PC's IP address on the Android app and connect

### Build

#### Android

```bash
cd ExtendScreenAndroid
./gradlew assembleDebug
```

The APK will be at `app/build/outputs/apk/debug/`

#### Windows

```bash
cd ExtendScreenServer
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o output_single
```

### Tech Stack

- **Android**: Kotlin, MediaCodec hardware decoding, SurfaceView rendering
- **Windows**: C# / WPF, .NET 7, GDI+ screen capture, TCP communication
- **Communication**: TCP Socket, ADB port forwarding, JPEG frame streaming

## License

MIT

---

*This is a fork of the original repository: https://github.com/tdtudo/ExtendScreen/

---

## Русский

Используйте ваше Android-устройство в качестве дополнительного экрана для Windows-ПК с поддержкой подключения по USB и Wi-Fi.

### Возможности

- Расширенный экран: используйте планшет/телефон как второй монитор для ПК
- USB-подключение: низкая задержка через перенаправление портов ADB
- Wi-Fi-подключение: беспроводное расширение, кабель не нужен
- ПК-к-ПК: расширение на другой Windows-ПК в качестве дополнительного дисплея
- Поддержание экрана включённым: предотвращает засыпание Android-устройства во время подключения
- Синхронизация мыши: на расширенном экране отображается обычный курсор

### Структура проекта

```
ExtendScreenAndroid/    # Android-приложение — принимает и отображает видеопоток
ExtendScreenServer/     # Windows-сервер — захватывает экран и передаёт поток
ExtendScreenClient/     # Windows-клиент — работает как расширенный дисплей на другом ПК
```

### Принцип работы

1. Windows-сервер захватывает область экрана, кодирует её в JPEG-кадры и отправляет по TCP
2. Android-приложение принимает JPEG-кадры, декодирует их аппаратным декодером и выводит на SurfaceView
3. Android-приложение фиксирует события касания/ввода и отправляет их обратно на Windows-сервер для эмуляции мыши
4. Связь устанавливается через перенаправление портов ADB (USB) или TCP в локальной сети (Wi-Fi)

### Использование

#### Предварительные требования

- Windows 10/11 (x64)
- Android 8.0+ устройство
- ADB (Android Debug Bridge)
- Включённая отладка по USB (для USB-подключения)

#### USB-подключение

1. Подключите Android-устройство к ПК с помощью USB-кабеля
2. Включите отладку по USB
3. Запустите Windows-приложение и выберите режим USB
4. Нажмите «Подключиться» в Android-приложении

#### Wi-Fi-подключение

1. Убедитесь, что ПК и Android-устройство находятся в одной локальной сети
2. Запустите Windows-приложение и выберите режим Wi-Fi
3. Введите IP-адрес ПК в Android-приложении и подключитесь

### Сборка

#### Android

```bash
cd ExtendScreenAndroid
./gradlew assembleDebug
```

APK-файл будет находиться в `app/build/outputs/apk/debug/`

#### Windows

```bash
cd ExtendScreenServer
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o output_single
```

### Технологический стек

- **Android**: Kotlin, аппаратное декодирование MediaCodec, рендеринг SurfaceView
- **Windows**: C# / WPF, .NET 7, захват экрана GDI+, TCP-связь
- **Сеть**: TCP Socket, перенаправление портов ADB, потоковая передача JPEG-кадров

## Лицензия

MIT

---

*Это форк оригинального репозитория: https://github.com/tdtudo/ExtendScreen/*