terraform {
  required_version = "~> 1.9.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "3.110.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "3.6.2"
    }
  }

  # placeholder names; use terraform init -backend=false to validate locally
  backend "azurerm" {
    resource_group_name  = "rg-parcel-trace-tfstate"
    storage_account_name = "stparceltracetfstate"
    container_name       = "tfstate"
    key                  = "dev.terraform.tfstate"
  }
}

provider "azurerm" {
  subscription_id = var.subscription_id

  features {
    key_vault {
      purge_soft_delete_on_destroy = true
    }
    resource_group {
      prevent_deletion_if_contains_resources = false
    }
  }
}

locals {
  # <type>-parcel-trace-<env>, compact form where only alphanumerics are allowed
  name    = "parcel-trace-${var.environment}"
  compact = "parceltrace${var.environment}"

  tags = merge({
    application = "parcel-trace"
    environment = var.environment
    managed-by  = "terraform"
    cost-center = var.cost_center
  }, var.extra_tags)
}

module "resource_group" {
  source   = "../../modules/resource-group"
  name     = "rg-${local.name}"
  location = var.location
  tags     = local.tags
}

module "log_analytics" {
  source              = "../../modules/log-analytics"
  name                = "log-${local.name}"
  location            = module.resource_group.location
  resource_group_name = module.resource_group.name
  retention_in_days   = 30
  daily_quota_gb      = 2
  tags                = local.tags
}

module "acr" {
  source              = "../../modules/acr"
  name                = "acr${local.compact}"
  location            = module.resource_group.location
  resource_group_name = module.resource_group.name
  sku                 = "Basic"
  tags                = local.tags
}

module "aks" {
  source                     = "../../modules/aks"
  name                       = "aks-${local.name}"
  dns_prefix                 = local.name
  location                   = module.resource_group.location
  resource_group_name        = module.resource_group.name
  kubernetes_version         = var.kubernetes_version
  sku_tier                   = "Free"
  system_node_vm_size        = "Standard_B2s"
  system_node_min_count      = 1
  system_node_max_count      = 1
  user_node_vm_size          = "Standard_D2s_v5"
  user_node_min_count        = 1
  user_node_max_count        = 2
  log_analytics_workspace_id = module.log_analytics.id
  acr_id                     = module.acr.id
  tags                       = local.tags
}

module "postgres" {
  source              = "../../modules/postgres-flexible"
  name                = "psql-${local.name}"
  location            = module.resource_group.location
  resource_group_name = module.resource_group.name
  sku_name            = "B_Standard_B1ms"
  storage_mb          = 32768
  tags                = local.tags
}

resource "random_password" "jwt_signing_key" {
  length  = 64
  special = false
}

module "keyvault" {
  source              = "../../modules/keyvault"
  name                = "kv-parceltrace-${var.environment}"
  location            = module.resource_group.location
  resource_group_name = module.resource_group.name
  reader_object_ids = {
    aks-secrets-provider = module.aks.key_vault_secrets_provider_object_id
  }
  # same keys as secretEnv in the helm values
  secrets = {
    jwt-signing-key = random_password.jwt_signing_key.result
    identity-db     = "Host=${module.postgres.fqdn};Database=identity;Username=${module.postgres.administrator_login};Password=${module.postgres.administrator_password};SSL Mode=Require"
    shipments-db    = "Host=${module.postgres.fqdn};Database=shipments;Username=${module.postgres.administrator_login};Password=${module.postgres.administrator_password};SSL Mode=Require"
    tracking-db-url = "postgres://${module.postgres.administrator_login}:${urlencode(module.postgres.administrator_password)}@${module.postgres.fqdn}:5432/tracking?sslmode=require"
    mongo-url       = var.mongo_connection_string
    redis-url       = var.redis_connection_string
  }
  tags = local.tags
}
