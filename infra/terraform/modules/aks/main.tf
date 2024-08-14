resource "azurerm_virtual_network" "this" {
  name                = "vnet-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  address_space       = [var.vnet_cidr]
  tags                = var.tags
}

# azure CNI: pods get IPs from this subnet
resource "azurerm_subnet" "nodes" {
  name                 = "snet-aks-nodes"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.node_subnet_cidr]
}

resource "azurerm_kubernetes_cluster" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  dns_prefix          = var.dns_prefix
  kubernetes_version  = var.kubernetes_version
  sku_tier            = var.sku_tier

  oidc_issuer_enabled               = true
  workload_identity_enabled         = true
  role_based_access_control_enabled = true
  azure_policy_enabled              = true

  default_node_pool {
    name                         = "system"
    vm_size                      = var.system_node_vm_size
    vnet_subnet_id               = azurerm_subnet.nodes.id
    enable_auto_scaling          = true
    min_count                    = var.system_node_min_count
    max_count                    = var.system_node_max_count
    max_pods                     = 50
    os_disk_size_gb              = 64
    only_critical_addons_enabled = true
    temporary_name_for_rotation  = "systemtmp"
    zones                        = var.zones
    tags                         = var.tags

    upgrade_settings {
      max_surge = "33%"
    }
  }

  identity {
    type = "SystemAssigned"
  }

  network_profile {
    network_plugin    = "azure"
    network_policy    = "azure"
    load_balancer_sku = "standard"
    service_cidr      = var.service_cidr
    dns_service_ip    = var.dns_service_ip
  }

  oms_agent {
    log_analytics_workspace_id = var.log_analytics_workspace_id
  }

  key_vault_secrets_provider {
    secret_rotation_enabled  = true
    secret_rotation_interval = "5m"
  }

  tags = var.tags

  lifecycle {
    ignore_changes = [
      default_node_pool[0].node_count,
    ]
  }
}

resource "azurerm_kubernetes_cluster_node_pool" "user" {
  name                  = "apps"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.this.id
  mode                  = "User"
  vm_size               = var.user_node_vm_size
  vnet_subnet_id        = azurerm_subnet.nodes.id
  enable_auto_scaling   = true
  min_count             = var.user_node_min_count
  max_count             = var.user_node_max_count
  max_pods              = 50
  os_disk_size_gb       = 64
  zones                 = var.zones
  node_labels = {
    "workload" = "parcel-trace"
  }
  tags = var.tags

  lifecycle {
    ignore_changes = [node_count]
  }
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                            = var.acr_id
  role_definition_name             = "AcrPull"
  principal_id                     = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
  skip_service_principal_aad_check = true
}
