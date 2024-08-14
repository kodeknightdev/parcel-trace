output "id" {
  value = azurerm_kubernetes_cluster.this.id
}

output "name" {
  value = azurerm_kubernetes_cluster.this.name
}

output "oidc_issuer_url" {
  description = "For federated credentials (workload identity)."
  value       = azurerm_kubernetes_cluster.this.oidc_issuer_url
}

output "kubelet_object_id" {
  value = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
}

output "key_vault_secrets_provider_object_id" {
  description = "Identity of the Key Vault CSI driver add-on; needs read access to the vault."
  value       = azurerm_kubernetes_cluster.this.key_vault_secrets_provider[0].secret_identity[0].object_id
}

output "node_subnet_id" {
  value = azurerm_subnet.nodes.id
}
