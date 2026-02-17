#!/bin/bash
# deploy_server.sh - Script de déploiement automatique du serveur Ryvax

set -e  # Exit on error

# Configuration
SERVER_IP="${1:-}"
SERVER_USER="${2:-ryvaxserver}"
BUILD_PATH="${3:-./Build/LinuxServer}"
SERVER_PORT="${4:-7777}"

# Couleurs pour les messages
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Vérifier les arguments
if [ -z "$SERVER_IP" ]; then
    print_error "Usage: $0 <SERVER_IP> [SERVER_USER] [BUILD_PATH] [SERVER_PORT]"
    echo "Example: $0 192.168.1.100 ryvaxserver ./Build/LinuxServer 7777"
    exit 1
fi

print_info "Déploiement du serveur Ryvax"
print_info "Serveur: $SERVER_USER@$SERVER_IP"
print_info "Build: $BUILD_PATH"
print_info "Port: $SERVER_PORT"

# Vérifier que le build existe
if [ ! -d "$BUILD_PATH" ]; then
    print_error "Le dossier de build n'existe pas: $BUILD_PATH"
    print_error "Créez d'abord un build Linux depuis Unity (File -> Build Settings)"
    exit 1
fi

# Détecter le nom de l'exécutable
EXEC_NAME=$(basename "$(ls "$BUILD_PATH"/*.x86_64 2>/dev/null | head -1)" 2>/dev/null)
if [ -z "$EXEC_NAME" ]; then
    print_error "Aucun exécutable .x86_64 trouvé dans $BUILD_PATH"
    exit 1
fi
print_info "Exécutable détecté: $EXEC_NAME"

# Compresser le build
print_info "Compression du build..."
tar -czf RyvaxServer.tar.gz -C "$(dirname "$BUILD_PATH")" "$(basename "$BUILD_PATH")"
print_info "Build compressé: RyvaxServer.tar.gz ($(du -h RyvaxServer.tar.gz | cut -f1))"

# Configuration SSH (ubuntu pour OVH, peut être changé)
SSH_USER="${SSH_USER:-ubuntu}"

# Transférer le build
print_info "Transfert du build vers $SERVER_IP..."
scp RyvaxServer.tar.gz $SSH_USER@$SERVER_IP:/tmp/

# Installer et configurer sur le serveur
print_info "Installation sur le serveur..."
ssh $SSH_USER@$SERVER_IP << EOF
    set -e

    # Créer l'utilisateur si nécessaire
    if ! id "$SERVER_USER" &>/dev/null; then
        echo "Création de l'utilisateur $SERVER_USER..."
        sudo useradd -m -s /bin/bash $SERVER_USER
    fi

    # Extraire le build
    echo "Extraction du build..."
    sudo rm -rf /home/$SERVER_USER/RyvaxServer
    sudo tar -xzf /tmp/RyvaxServer.tar.gz -C /home/$SERVER_USER/

    # Renommer le dossier extrait en RyvaxServer (le tar contient $(basename "$BUILD_PATH"))
    if [ -d "/home/$SERVER_USER/$(basename "$BUILD_PATH")" ] && [ "$(basename "$BUILD_PATH")" != "RyvaxServer" ]; then
        sudo mv "/home/$SERVER_USER/$(basename "$BUILD_PATH")" /home/$SERVER_USER/RyvaxServer
    fi

    # Fixer les permissions
    sudo chown -R $SERVER_USER:$SERVER_USER /home/$SERVER_USER/RyvaxServer
    sudo find /home/$SERVER_USER/RyvaxServer -name "*.x86_64" -exec chmod +x {} \;

    # Installer les dépendances si nécessaire
    if ! dpkg -l | grep -q libgdiplus; then
        echo "Installation des dépendances Unity..."
        sudo apt update
        sudo apt install -y libgdiplus libc6-dev libx11-6 libxcursor1 libxrandr2 screen
    fi

    # Configurer le firewall
    if command -v ufw &> /dev/null; then
        echo "Configuration du firewall..."
        sudo ufw allow $SERVER_PORT/udp
        sudo ufw allow 22/tcp
        sudo ufw --force enable
    fi

    # Créer le service systemd
    echo "Création du service systemd..."
    sudo tee /etc/systemd/system/ryvaxserver.service > /dev/null << SERVICEEOF
[Unit]
Description=Ryvax Game Server
After=network.target

[Service]
Type=simple
User=$SERVER_USER
WorkingDirectory=/home/$SERVER_USER/RyvaxServer
ExecStart=/home/$SERVER_USER/RyvaxServer/$EXEC_NAME -batchmode -nographics -server -port $SERVER_PORT
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
SERVICEEOF

    # Recharger systemd et démarrer le service
    sudo systemctl daemon-reload
    sudo systemctl enable ryvaxserver
    sudo systemctl restart ryvaxserver

    # Nettoyer
    sudo rm /tmp/RyvaxServer.tar.gz

    echo "Déploiement terminé!"
EOF

# Nettoyer localement
rm RyvaxServer.tar.gz

print_info "✅ Déploiement réussi!"
print_info "Le serveur écoute sur: $SERVER_IP:$SERVER_PORT"
print_info ""
print_info "Commandes utiles:"
echo "  Voir les logs:        ssh $SSH_USER@$SERVER_IP 'sudo journalctl -u ryvaxserver -f'"
echo "  Redémarrer:           ssh $SSH_USER@$SERVER_IP 'sudo systemctl restart ryvaxserver'"
echo "  Arrêter:              ssh $SSH_USER@$SERVER_IP 'sudo systemctl stop ryvaxserver'"
echo "  Statut:               ssh $SSH_USER@$SERVER_IP 'sudo systemctl status ryvaxserver'"
