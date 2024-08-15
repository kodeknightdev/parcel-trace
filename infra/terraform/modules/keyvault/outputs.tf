output "id" {
  value = azurerm_key_vault.this.id
}

output "uri" {
  value = azurerm_key_vault.this.vault_uri
}

output "secret_names" {
  value = [for s in azurerm_key_vault_secret.this : s.name]
}
