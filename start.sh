#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST_PATH="$ROOT_DIR/deploy/k8s/chat-platform-k3s.yaml"
NAMESPACE="chat-platform"
HOST_NAME="chat.local"
HOSTS_MARKER="# chat-platform-k3s"
DEFAULT_KUBECONFIG="/etc/rancher/k3s/k3s.yaml"

IMAGES=(
  "bvilela/chat-identity:latest|src/IdentityService/API/Dockerfile"
  "bvilela/chat-write:latest|src/ChatService/API/Dockerfile"
  "bvilela/chat-message:latest|src/MessageService/API/Dockerfile"
  "bvilela/chat-presence:latest|src/PresenceService/API/Dockerfile"
  "bvilela/chat-notification:latest|src/NotificationService/API/Dockerfile"
  "bvilela/chat-gateway:latest|src/ApiGateway/Dockerfile"
  "bvilela/chat-frontend:latest|frontend/Dockerfile"
)

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

build_images() {
  local item image dockerfile

  for item in "${IMAGES[@]}"; do
    IFS='|' read -r image dockerfile <<< "$item"
    echo "[build] $image"
    docker build -t "$image" -f "$ROOT_DIR/$dockerfile" "$ROOT_DIR"
  done
}

import_images_into_k3s() {
  local image_names=()
  local item image dockerfile

  for item in "${IMAGES[@]}"; do
    IFS='|' read -r image dockerfile <<< "$item"
    image_names+=("$image")
  done

  echo "[import] Importing Docker images into k3s containerd"
  docker save "${image_names[@]}" | sudo k3s ctr images import -
}

wait_for_rollouts() {
  local deployment
  local deployments=(
    postgres-identity
    postgres-message
    redis
    rabbitmq
    otel-collector
    identity-service
    chat-service
    message-service
    presence-service
    notification-service
    api-gateway
    frontend
  )

  for deployment in "${deployments[@]}"; do
    echo "[rollout] deployment/$deployment"
    kubectl rollout status "deployment/$deployment" -n "$NAMESPACE" --timeout=300s
  done
}

ensure_hosts_entry() {
  local node_ip

  if [[ "${SKIP_HOSTS_UPDATE:-0}" == "1" ]]; then
    return
  fi

  node_ip="$(kubectl get nodes -o jsonpath='{.items[0].status.addresses[?(@.type=="ExternalIP")].address}')"
  if [[ -z "$node_ip" ]]; then
    node_ip="$(kubectl get nodes -o jsonpath='{.items[0].status.addresses[?(@.type=="InternalIP")].address}')"
  fi

  if [[ -z "$node_ip" ]]; then
    echo "[hosts] Could not determine the k3s node IP. Add $HOST_NAME manually." >&2
    return
  fi

  if grep -Fq "$HOSTS_MARKER" /etc/hosts; then
    local temp_file
    temp_file="$(mktemp)"
    grep -vF "$HOSTS_MARKER" /etc/hosts > "$temp_file"
    printf '%s %s %s\n' "$node_ip" "$HOST_NAME" "$HOSTS_MARKER" >> "$temp_file"
    sudo cp "$temp_file" /etc/hosts
    rm -f "$temp_file"
    echo "[hosts] Updated existing $HOST_NAME entry to $node_ip"
    return
  fi

  if grep -Eq "(^|[[:space:]])$HOST_NAME([[:space:]]|$)" /etc/hosts; then
    echo "[hosts] Existing manual entry for $HOST_NAME detected. Leaving /etc/hosts untouched."
    return
  fi

  printf '%s %s %s\n' "$node_ip" "$HOST_NAME" "$HOSTS_MARKER" | sudo tee -a /etc/hosts >/dev/null
  echo "[hosts] Added $HOST_NAME -> $node_ip"
}

main() {
  if [[ -z "${KUBECONFIG:-}" && -f "$DEFAULT_KUBECONFIG" ]]; then
    export KUBECONFIG="$DEFAULT_KUBECONFIG"
  fi

  require_command docker
  require_command kubectl
  require_command sudo
  require_command k3s

  kubectl cluster-info >/dev/null
  kubectl get nodes >/dev/null

  if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
    build_images
  fi

  if [[ "${SKIP_IMPORT:-0}" != "1" ]]; then
    import_images_into_k3s
  fi

  echo "[apply] Applying $MANIFEST_PATH"
  kubectl apply -f "$MANIFEST_PATH"

  wait_for_rollouts
  ensure_hosts_entry

  echo
  echo "Application is ready."
  echo "Frontend: http://$HOST_NAME"
  echo "API Gateway: kubectl port-forward svc/api-gateway 8080:8080 -n $NAMESPACE"
  echo "RabbitMQ UI: kubectl port-forward svc/rabbitmq 15672:15672 -n $NAMESPACE"
}

main "$@"