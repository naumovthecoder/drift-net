# ⚡ DriftNet - Быстрый старт

## 🚀 За 3 минуты до работающей P2P сети

### 1️⃣ Запуск системы (30 секунд)
```bash
make -C drift-control-deck reset
```

### 2️⃣ Создание P2P сети (30 секунд)
1. Открыть http://localhost:5173
2. Выбрать 10 узлов
3. Нажать "🚀 Launch / Update Network"

### 3️⃣ Создание тестового файла (10 секунд)
```bash
echo "Hello DriftNet P2P!" > test.txt
```

### 4️⃣ Загрузка в сеть (30 секунд)
```bash
docker cp test.txt drift-client:/app/test.txt
docker exec -i drift-client dotnet DriftClient.dll << 'EOF'
upload /app/test.txt
exit
EOF
```

### 5️⃣ Наблюдение метрик (в реальном времени)
Обновить http://localhost:5173 и смотреть:
- 📥 **Received** - растет каждую секунду
- 📤 **Forwarded** - показывает активность  
- 🔄 **Circulating** - циркуляция чанков
- 🚫 **Loops Dropped** - отброшенные дубликаты

## ✅ Готово!
Ваша P2P сеть работает и чанки циркулируют между узлами автоматически!

---

### 🔥 Полезные команды

**Статус системы:**
```bash
curl -s http://localhost:5080/api/metrics | jq '.[0] | {id, received: .chunks, forwarded, circulating}'
```

**Логи в реальном времени:**
```bash
docker logs backend-driftnode-1 -f | grep CIRCULATE
```

**Остановка:**
```bash
make -C drift-control-deck clean
``` 