# Архитектура

## Слои

- `Assets/Game/Core` — независимая от сцен расчётная логика: движение к целевому значению, перезарядка, бюджет времени и дальности снаряда.
- `Assets/Game/Runtime` — компоненты, работающие во время игры: ввод, движение, башня, оружие, снаряд и камера.
- `Assets/Game/Editor` — воспроизводимое создание Unity-ассетов, префабов и тестовой сцены.
- `Assets/Game/Tests/EditMode` — быстрые тесты расчётной логики без запуска полноценной сцены.
- `Assets/Game/Tests/PlayMode` — проверки жизненного цикла Unity-объектов и загрузки тестовой сцены.
- `Assets/Game/Data` — ScriptableObject-конфигурации, редактируемые в Inspector.

## Поток управления

`PlayerTankInput` читает новый Input System и передаёт две оси в `TankMotor`, мировую позицию курсора в `TurretAiming`, а команду выстрела — в `WeaponController`.

`TankMotor` меняет положение и угол через `Rigidbody2D` в `FixedUpdate`. `TurretAiming` вращает отдельный `TurretPivot`, поэтому башня не меняет ориентацию корпуса.

`WeaponController` проверяет `ReloadTimer`, создаёт `Projectile2D` в точке `Muzzle` и передаёт ему параметры из `TankPrototypeConfig`. Снаряд на каждом физическом шаге проверяет весь путь через `Physics2D.CircleCastAll`.

## Данные

В Sprint 001 используется один `TankPrototypeConfig`. Runtime-код не знает о Tiger II и может работать с другой машиной при подстановке другого конфига и визуального префаба. В Sprint 002 этот слой должен превратиться в общую систему определений машин.

## Воспроизводимость Unity-ассетов

`PrototypeSetup.BuildAll` доступен через меню `Local Tanks > Build First Sprint Prototype`. Инструмент через Unity Editor API:

1. настраивает и нарезает исходную текстуру;
2. создаёт или обновляет конфиг;
3. создаёт префаб снаряда;
4. создаёт префаб Tiger II;
5. создаёт `Battle_TestRange` и добавляет её в Build Settings.

Сгенерированные `.meta`, `.asset`, `.prefab` и `.unity` не редактируются вручную.
