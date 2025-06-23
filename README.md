# 🚀 DriftNet - P2P File Network

**DriftNet** - это децентрализованная P2P сеть для распределения файлов с активной циркуляцией данных между узлами.

## 🎯 Особенности

- ✅ **Истинная P2P архитектура** - данные циркулируют между узлами
- ✅ **Автоматическая циркуляция** - чанки продолжают перемещаться в фоне  
- ✅ **Loop detection** - предотвращение бесконечных петель
- ✅ **Веб-интерфейс** - красивый дашборд с метриками в реальном времени
- ✅ **Масштабируемость** - поддержка до 100+ узлов
- ✅ **Docker контейнеризация** - простое развертывание

## 🚀 Быстрый старт

### 1. Запуск системы

```bash
# Запуск веб-интерфейса и backend
make -C drift-control-deck reset

# Система будет доступна по адресам:
# - Веб-интерфейс: http://localhost:5173
# - API: http://localhost:5080
```

### 2. Создание P2P сети

1. **Откройте браузер** → http://localhost:5173
2. **Выберите количество узлов** (рекомендуется 5-20 для тестирования)
3. **Нажмите "🚀 Launch / Update Network"**
4. **Дождитесь запуска** - узлы появятся в таблице метрик

### 3. Подготовка тестового файла

```bash
# Создание маленького файла для быстрого тестирования
echo "Hello DriftNet! This is a test file for P2P network." > test-file.txt

# Или создание файла с несколькими строками
cat > test-file.txt << 'EOF'
DriftNet P2P Network Test File
==============================
This file demonstrates the P2P capabilities.
Data will circulate between network nodes automatically.
Timestamp: $(date)
EOF

# Проверка размера файла
ls -lh test-file.txt
```

### 4. Загрузка файла в сеть

```bash
# Копирование файла в контейнер клиента
docker cp test-file.txt drift-client:/app/test-file.txt

# Загрузка файла в P2P сеть
docker exec -i drift-client dotnet DriftClient.dll << 'EOF'
upload /app/test-file.txt
exit
EOF
```

### 5. Наблюдение за метриками

После загрузки файла откройте веб-интерфейс и наблюдайте:

- 📥 **Received** - количество полученных чанков узлом
- 📤 **Forwarded** - количество переданных чанков
- 🔄 **Circulating** - чанки, циркулирующие в фоне  
- ⚡ **Avg TTL** - средний Time-To-Live чанков
- 🚫 **Loops Dropped** - отброшенные дубликаты

## 📊 Мониторинг активности

### Веб-интерфейс
Открыть http://localhost:5173 для просмотра:
- Реалтайм метрики всех узлов
- Общая статистика сети
- Автообновление каждые 2 секунды

### API метрики
```bash
# Получение всех метрик
curl -s http://localhost:5080/api/metrics | jq .

# Топ-5 наиболее активных узлов
curl -s http://localhost:5080/api/metrics | jq 'sort_by(.chunks) | reverse | .[0:5]'

# Метрики конкретного узла
curl -s http://localhost:5080/api/metrics | jq '.[] | select(.id == "backend-driftnode-1")'
```

### Логи в реальном времени
```bash
# Общие логи циркуляции
docker compose logs -f | grep -E "(RECEIVED|FORWARDED|CIRCULATE|LOOP)"

# Логи конкретного узла
docker logs backend-driftnode-1 -f | grep -E "(RECEIVED|FORWARDED|CIRCULATE)"
```

## 🔧 Расширенное использование

### Восстановление файлов

```bash
# Восстановление файла из сети (по префиксу чанков)
docker exec -i drift-client dotnet DriftClient.dll << 'EOF'
get chunk --out recovered-file.txt
exit
EOF

# Проверка восстановленного файла
docker exec drift-client cat /app/recovered-file.txt
```

### Настройка параметров сети

Переменные окружения в `docker-compose.yml`:

```yaml
environment:
  - RECENT_IDS_MAX=500          # Размер кэша для loop detection
  - RECENT_ID_TTL_SEC=10        # TTL кэша в секундах  
  - SEND_DELAY_MIN_MS=10        # Минимальная задержка пересылки
  - SEND_DELAY_MAX_MS=50        # Максимальная задержка пересылки
```

### Масштабирование сети

```bash
# Запуск большой сети (50 узлов)  
# В веб-интерфейсе выберите 50 узлов и нажмите Launch

# Проверка количества активных узлов
docker ps | grep backend-driftnode | wc -l
```

## 🛠️ Управление системой

### Остановка сети
```bash
make -C drift-control-deck clean
```

### Полная очистка
```bash
# Остановка всех контейнеров и очистка
make -C drift-control-deck clean

# Дополнительная очистка Docker
docker system prune -f
```

### Перезапуск с обновлениями
```bash
make -C drift-control-deck reset
```

## 📁 Структура проекта

```
DriftNet/
├── drift-control-deck/          # Веб-интерфейс управления
│   ├── frontend/               # React фронтенд
│   ├── backend/                # .NET API backend  
│   └── Makefile               # Команды управления
├── DriftNode/                  # P2P узел сети
├── DriftClient/               # Клиент для загрузки/скачивания
├── DriftCoordinator/          # Координатор узлов
└── docker-compose.yml         # Конфигурация контейнеров
```

## 🚨 Решение проблем

### Порты заняты
```bash
# Проверка занятых портов
lsof -i :5080
lsof -i :5173  

# Принудительная очистка
make -C drift-control-deck clean
```

### Узлы не запускаются
```bash
# Проверка логов координатора
docker logs coordinator

# Проверка сетевого подключения
docker network ls | grep drift
```

### Нет циркуляции
```bash
# Проверка что файл загружен
docker exec drift-client ls -la /app/

# Проверка активности узлов
curl -s http://localhost:5080/api/metrics | jq '.[] | select(.chunks > 0)'
```

## 📈 Оптимизация производительности

### Для тестирования (быстрые результаты)
- **Узлы:** 5-10
- **TTL:** 50
- **Размер файла:** до 1KB

### Для нагрузочного тестирования
- **Узлы:** 20-50  
- **TTL:** 100-500
- **Размер файла:** до 100MB

### Для продакшена
- **Узлы:** 50-100+
- **TTL:** 1000+
- **Файлы:** любого размера

## 🎉 Результаты

После запуска вы увидите:
- 📊 **Живые метрики** в веб-интерфейсе
- 🔄 **Активную циркуляцию** чанков между узлами
- ⚡ **Высокую производительность** P2P сети
- 🛡️ **Стабильную работу** без петель и зависаний

---

**Наслаждайтесь распределенной P2P сетью DriftNet! 🚀** 