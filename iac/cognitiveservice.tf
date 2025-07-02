resource "azurerm_cognitive_account" "rkw-fr" {
  name                = "ai-rkw-fr"
  location            = azurerm_resource_group.rkw.location
  resource_group_name = azurerm_resource_group.rkw.name
  kind                = "FormRecognizer"

  sku_name              = "F0"
  custom_subdomain_name = "rkwjournalreaderfr"

  # use storage account for this rg
  # storage {
  #   storage_account_id = azurerm_storage_account.rkw.id
  # }

  # Enable a system‐assigned managed identity
  identity {
    type = "SystemAssigned"
  }

  tags = local.tags
}

resource "azurerm_key_vault_secret" "api_key_1_fr" {
  name         = "api-key-1-fr"
  value        = azurerm_cognitive_account.rkw-fr.primary_access_key
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_key_vault_secret" "api_key_2_fr" {
  name         = "api-key-2-fr"
  value        = azurerm_cognitive_account.rkw-fr.secondary_access_key
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_key_vault_secret" "endpoint_fr" {
  name         = "api-endpoint-fr"
  value        = azurerm_cognitive_account.rkw-fr.endpoint
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}
