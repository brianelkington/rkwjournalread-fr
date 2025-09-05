resource "azurerm_cognitive_account" "translator" {
  name                  = "ai-rkw-translator"
  location              = azurerm_resource_group.rkw.location
  resource_group_name   = azurerm_resource_group.rkw.name
  kind                  = "TextTranslation"
  sku_name              = "F0"
  custom_subdomain_name = "rkwjournaltranslator"

  identity {
    type = "SystemAssigned"
  }

  tags = local.tags
}

resource "azurerm_key_vault_secret" "translator_key" {
  name         = "translator-key"
  value        = azurerm_cognitive_account.translator.primary_access_key
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_key_vault_secret" "translator_endpoint" {
  name         = "translator-endpoint"
  value        = azurerm_cognitive_account.translator.endpoint
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_key_vault_secret" "translator_region" {
  name         = "translator-region"
  value        = azurerm_cognitive_account.translator.location
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}