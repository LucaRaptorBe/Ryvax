# Scripts de déploiement serveur Ryvax

Scripts utiles pour build et déployer le serveur de jeu Ryvax.

## Scripts disponibles

### 1. `build_server.sh` - Build le serveur Linux

Crée automatiquement un build serveur Linux headless depuis Unity.

```bash
./Scripts/build_server.sh
```

**Variables d'environnement:**
- `UNITY_PATH`: Chemin vers l'exécutable Unity (détecté automatiquement sur macOS)

**Sortie:** `./Build/LinuxServer/RyvaxServer.x86_64`

---

### 2. `deploy_server.sh` - Déploie sur un VPS

Déploie automatiquement le build serveur sur un VPS et configure systemd.

```bash
./Scripts/deploy_server.sh <SERVER_IP> [USER] [BUILD_PATH] [PORT]
```

**Exemples:**
```bash
# Déploiement basique
./Scripts/deploy_server.sh 192.168.1.100

# Avec paramètres personnalisés
./Scripts/deploy_server.sh 192.168.1.100 gameserver ./Build/LinuxServer 8888
```

**Ce que fait le script:**
1. Compresse le build
2. Transfère via SCP
3. Crée l'utilisateur serveur
4. Installe les dépendances
5. Configure le firewall (ufw)
6. Crée et démarre le service systemd

**Prérequis:**
- Accès SSH root au serveur
- Build Linux créé (utilisez `build_server.sh` d'abord)

---

### 3. `test_connection.sh` - Test la connexion

Vérifie que le serveur est accessible et fonctionne.

```bash
./Scripts/test_connection.sh <SERVER_IP> [PORT]
```

**Exemple:**
```bash
./Scripts/test_connection.sh 192.168.1.100 7777
```

**Tests effectués:**
1. ✅ Ping (serveur accessible)
2. ✅ Port UDP ouvert
3. ✅ Accès SSH
4. ✅ Service actif

---

## Workflow complet

### Configuration initiale

1. **Installer le composant ServerLauncher dans Unity**
   - Ouvrir la scène principale
   - Ajouter le composant `ServerLauncher` sur un GameObject
   - Assigner la référence `ServerGameLoop`

2. **Créer le build serveur**
   ```bash
   ./Scripts/build_server.sh
   ```

3. **Obtenir un VPS**
   - DigitalOcean, AWS Lightsail, Linode, etc.
   - Ubuntu 22.04 LTS recommandé
   - Minimum: 2GB RAM, 1 vCPU

4. **Déployer sur le VPS**
   ```bash
   ./Scripts/deploy_server.sh YOUR_SERVER_IP
   ```

5. **Tester la connexion**
   ```bash
   ./Scripts/test_connection.sh YOUR_SERVER_IP
   ```

### Mise à jour du serveur

```bash
# 1. Rebuild
./Scripts/build_server.sh

# 2. Redéployer
./Scripts/deploy_server.sh YOUR_SERVER_IP
```

### Commandes serveur utiles

```bash
# Voir les logs en temps réel
ssh root@YOUR_SERVER_IP "journalctl -u ryvaxserver -f"

# Redémarrer le serveur
ssh root@YOUR_SERVER_IP "systemctl restart ryvaxserver"

# Arrêter le serveur
ssh root@YOUR_SERVER_IP "systemctl stop ryvaxserver"

# Démarrer le serveur
ssh root@YOUR_SERVER_IP "systemctl start ryvaxserver"

# Voir le statut
ssh root@YOUR_SERVER_IP "systemctl status ryvaxserver"

# Voir les derniers 50 logs
ssh root@YOUR_SERVER_IP "journalctl -u ryvaxserver -n 50"
```

---

## Configuration Unity

### ServerLauncher.cs

Le serveur démarre automatiquement en mode headless si:
- Flag `-batchmode` ou `-server` détecté
- `Application.isBatchMode == true`

**Arguments en ligne de commande supportés:**
- `-server` ou `--server`: Force le mode serveur
- `-port <number>` ou `--port <number>`: Définit le port

**Exemple de lancement manuel:**
```bash
./RyvaxServer.x86_64 -batchmode -nographics -server -port 7777
```

---

## Dépannage

### Build échoue

```bash
# Vérifier les logs
cat build_server.log | tail -n 50
```

**Solutions courantes:**
- Vérifier que Unity est installé
- Vérifier le chemin `UNITY_PATH`
- S'assurer que le projet Unity compile sans erreurs

### Déploiement échoue

**Erreur: "Permission denied"**
```bash
# S'assurer d'avoir les clés SSH configurées
ssh-copy-id root@YOUR_SERVER_IP
```

**Erreur: "Connection refused"**
- Vérifier que le serveur est accessible: `ping YOUR_SERVER_IP`
- Vérifier que SSH est activé sur le VPS

### Le serveur ne répond pas

```bash
# Vérifier les logs
ssh root@YOUR_SERVER_IP "journalctl -u ryvaxserver -n 100"

# Vérifier que le port est ouvert
./Scripts/test_connection.sh YOUR_SERVER_IP

# Vérifier le firewall du VPS provider
# AWS: Security Groups
# DigitalOcean: Firewalls
# Assurez-vous que le port UDP 7777 est ouvert
```

### Clients ne peuvent pas se connecter

1. **Vérifier l'adresse IP du serveur**
   ```bash
   ssh root@YOUR_SERVER_IP "curl ifconfig.me"
   ```

2. **Vérifier que le port est ouvert depuis l'extérieur**
   ```bash
   # Depuis votre machine locale
   nc -zvu YOUR_SERVER_IP 7777
   ```

3. **Vérifier les logs du serveur**
   ```bash
   ssh root@YOUR_SERVER_IP "journalctl -u ryvaxserver -f"
   # Puis essayer de se connecter depuis Unity
   ```

---

## Coûts estimés

| Fournisseur | Specs | Prix/mois | Players simultanés |
|-------------|-------|-----------|-------------------|
| DigitalOcean | 2GB RAM, 1 vCPU | $12 | 20-40 |
| AWS Lightsail | 2GB RAM, 1 vCPU | $10 | 20-40 |
| Linode | 2GB RAM, 1 vCPU | $10 | 20-40 |
| Hetzner | 4GB RAM, 2 vCPU | €4.5 | 40-80 |
| Vultr | 2GB RAM, 1 vCPU | $12 | 20-40 |

**Note:** Pour un MOBA, un serveur peut généralement héberger 2-4 matches simultanés (10 joueurs par match).

---

## Prochaines étapes

Une fois le serveur en ligne et fonctionnel:

1. **Monitoring**
   - Installer Prometheus + Grafana
   - Monitorer CPU, RAM, bande passante
   - Alertes automatiques

2. **Matchmaking**
   - Implémenter un matchmaker centralisé
   - Gérer plusieurs serveurs de jeu
   - Load balancing

3. **CI/CD**
   - Automatiser le déploiement avec GitHub Actions
   - Tests automatiques avant déploiement

4. **Scaling**
   - Multi-régions (US, EU, Asia)
   - Auto-scaling basé sur la charge
   - Services managés (GameLift, Edgegap)

---

## Support

Pour plus d'informations, consultez:
- `../SERVER_DEPLOYMENT.md` - Guide détaillé de déploiement
- Documentation FishNet: https://fish-networking.gitbook.io/
- Documentation Unity Networking: https://docs.unity3d.com/Manual/UNet.html
