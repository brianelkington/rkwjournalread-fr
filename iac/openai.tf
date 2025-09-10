resource "azurerm_cognitive_account" "rkw-openai" {
  name                  = "ai-rkw-openai"
  location              = azurerm_resource_group.rkw.location
  resource_group_name   = azurerm_resource_group.rkw.name
  kind                  = "OpenAI"
  sku_name              = "S0"
  custom_subdomain_name = "rkwjournalopenai"

  identity {
    type = "SystemAssigned"
  }

  tags = local.tags
}

resource "azurerm_cognitive_deployment" "rkw-gpt" {
  name                 = "gpt-4o-mini"
  cognitive_account_id = azurerm_cognitive_account.rkw-openai.id

  model {
    format  = "OpenAI"
    name    = "gpt-4o-mini"
    version = "2021-04-30" // Use the latest available version for GPT-4o Mini
  }

  sku {
    name = "Standard"
  }
}

resource "azurerm_key_vault_secret" "openai_endpoint" {
  name         = "openai-endpoint"
  value        = azurerm_cognitive_account.rkw-openai.endpoint
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_key_vault_secret" "openai_key" {
  name         = "openai-key"
  value        = azurerm_cognitive_account.rkw-openai.primary_access_key
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}