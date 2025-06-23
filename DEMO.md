# 🎬 DriftNet - Демонстрация

## 📋 Что покажет демо:
- ✅ P2P сеть из 20 узлов
- ✅ Циркуляция чанков в реальном времени  
- ✅ Живые метрики в веб-интерфейсе
- ✅ Loop detection в действии

---

## 🎯 Демо сценарий (5 минут)

### Шаг 1: Запуск системы
```bash
# Терминал 1: Запуск веб-интерфейса
make -C drift-control-deck reset

# Ждем сообщение "Ready to accept connections"
```

### Шаг 2: Создание большой сети  
```bash
# Браузер: http://localhost:5173
# 1. Выбираем 20 узлов на слайдере
# 2. Жмем "🚀 Launch / Update Network"  
# 3. Ждем появления всех 20 узлов в таблице
```

### Шаг 3: Подготовка файлов
```bash
# Терминал 2: Создаем разные файлы для тестов

# Маленький файл
echo "Small test file" > small.txt

# Средний файл  
head -c 1000 /dev/urandom | base64 > medium.txt

# Большой файл (несколько чанков)
head -c 1000000 /dev/urandom | base64 > large.txt

ls -lh *.txt
```

### Шаг 4: Загрузка файлов и наблюдение
```bash
# Загружаем маленький файл
docker cp small.txt drift-client:/app/small.txt
docker exec -i drift-client dotnet DriftClient.dll << 'EOF'
upload /app/small.txt
exit
EOF

# Сразу идем в браузер и смотрим метрики!
# Обновляем страницу каждые 5 секунд
```

### Шаг 5: Мониторинг активности
```bash
# Терминал 3: Логи в реальном времени
docker logs backend-driftnode-1 --follow | grep -E "(RECEIVED|FORWARDED|CIRCULATE|LOOP)"

# Терминал 4: API метрики
watch -n 2 'curl -s http://localhost:5080/api/metrics | jq ".[0] | {id, received: .chunks, forwarded, circulating, loops: .loopsDropped}"'
```

### Шаг 6: Масштабирование  
```bash
# Загружаем больший файл
docker cp large.txt drift-client:/app/large.txt
docker exec -i drift-client dotnet DriftClient.dll << 'EOF'
upload /app/large.txt
exit
EOF

# Наблюдаем как метрики взлетают!
```

---

## 🎪 Ожидаемые результаты

### В веб-интерфейсе:
- **Узлы:** 20 активных
- **📥 Received:** Быстро растет (5-50+ за минуту)
- **📤 Forwarded:** Примерно равно Received
- **🔄 Circulating:** 70-90% от Forwarded  
- **🚫 Loops Dropped:** Растет (показывает что loop detection работает)

### В логах:
```
[RECEIVED] Chunk chunk-0001 | TTL=49 | Size=1024 bytes
[FORWARDED] Chunk chunk-0001 → 172.21.0.15:5000 (TTL=48)
[CIRCULATE] chunk-0001 continuing circulation (TTL=47)
[LOOP] dropped chunk-0001
```

### В API:
```json
{
  "id": "backend-driftnode-1",
  "received": 45,
  "forwarded": 43,
  "circulating": 38,
  "loops": 35
}
```

---

## 🔥 Крутые моменты для показа:

1. **Мгновенная реакция** - загрузили файл, через 2 секунды метрики взлетели
2. **Автоматическая циркуляция** - чанки продолжают двигаться сами по себе
3. **Loop detection** - система умная, не зацикливается  
4. **Реалтайм обновления** - все метрики живые, обновляются каждые 2 секунды
5. **Масштабируемость** - легко добавить больше узлов

---

## 🎭 Сценарий презентации:

> "Покажу как за 3 минуты создать настоящую P2P сеть из 20 узлов..."
> 
> "Файл разбивается на чанки и отправляется случайным узлам..."
> 
> "Смотрите - чанки начали циркулировать автоматически!"
> 
> "Каждый узел пересылает данные дальше и запускает фоновую циркуляцию..."
> 
> "Loop detection срабатывает - дубликаты отбрасываются..."
> 
> "Это живая децентрализованная сеть - данные всегда в движении!"

**Результат:** Впечатляющая демонстрация работающей P2P системы! 🚀 