#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST_PATH="$ROOT_DIR/deploy/k8s/chat-platform-k3s.yaml"
NAMESPACE="chat-platform"
HOSTS_MARKER="# chat-platform-k3s"
DEFAULT_KUBECONFIG="/etc/rancher/k3s/k3s.yaml"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

remove_hosts_entry() {
  local temp_file

  if [[ "${SKIP_HOSTS_UPDATE:-0}" == "1" ]]; then
    return
  fi

  if ! grep -Fq "$HOSTS_MARKER" /etc/hosts; then
    return
  fi

  temp_file="$(mktemp)"
  grep -vF "$HOSTS_MARKER" /etc/hosts > "$temp_file"
  sudo cp "$temp_file" /etc/hosts
  rm -f "$temp_file"
  echo "[hosts] Removed managed chat.local entry"
}

main() {
  if [[ -z "${KUBECONFIG:-}" && -f "$DEFAULT_KUBECONFIG" ]]; then
    export KUBECONFIG="$DEFAULT_KUBECONFIG"
  fi

  require_command kubectl
  require_command sudo

  if kubectl get namespace "$NAMESPACE" >/dev/null 2>&1; then
    echo "[delete] Removing resources from $MANIFEST_PATH"
    kubectl delete -f "$MANIFEST_PATH" --ignore-not-found=true
  else
    echo "[delete] Namespace $NAMESPACE does not exist. Nothing to remove."
  fi

  remove_hosts_entry
  echo "Application stopped."
}

main "$@"