LARP Client — обновление оформления

1. Закройте LARP Client и Minecraft.
2. Распакуйте весь архив в отдельную папку.
3. Запустите Install.cmd и выберите существующую папку LARP Client с launch-config.json.
4. Откройте обычный LARP Client.exe в папке клиента.

Новый EXE в этом архиве — обновление, для игры ему нужны прежние game и runtime.
Никнейм, память, миры и конфигурация запуска сохраняются.
В JAR заменяются только три PNG логотипа: обычный, сезонный и значок мода.
В backups создаётся резервная копия; Restore.ps1 в ней возвращает старые файлы.
Другая папка: powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -Target "путь к LARP Client"

Исходники: source\LarpClient.cs. Повторная сборка: source\build-launcher.ps1.
Проверено: сборка C# без предупреждений, отрисовка меню, установка и откат на тестовой копии.
Minecraft после обновления не запускался.
