output "resource_group_name" {
  value = module.resource_group.name
}

output "acr_login_server" {
  description = "Used as global.imageRegistry by the Helm deploy."
  value       = module.acr.login_server
}

output "aks_name" {
  value = module.aks.name
}

output "aks_oidc_issuer_url" {
  value = module.aks.oidc_issuer_url
}

output "postgres_fqdn" {
  value = module.postgres.fqdn
}

output "key_vault_uri" {
  value = module.keyvault.uri
}
