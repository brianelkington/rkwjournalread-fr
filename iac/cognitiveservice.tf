resource "azurerm_cognitive_account" "rkw-training" {
  name                = "ai-rkw-vision-training"
  location            = azurerm_resource_group.rkw.location
  resource_group_name = azurerm_resource_group.rkw.name
  kind                = "CustomVision.Training"

  sku_name              = "S0"
  custom_subdomain_name = "rkwjournalreadertraining"

  tags = local.tags
}

resource "azurerm_key_vault_secret" "api_key_1_training" {
  name         = "api-key-1-training"
  value        = azurerm_cognitive_account.rkw-training.primary_access_key
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_key_vault_secret" "api_key_2_training" {
  name         = "api-key-2-training"
  value        = azurerm_cognitive_account.rkw-training.secondary_access_key
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_key_vault_secret" "endpoint_training" {
  name         = "api-endpoint-training"
  value        = azurerm_cognitive_account.rkw-training.endpoint
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_cognitive_account" "rkw-prediction" {
  name                = "ai-rkw-vision-prediction"
  location            = azurerm_resource_group.rkw.location
  resource_group_name = azurerm_resource_group.rkw.name
  kind                = "CustomVision.Prediction"

  sku_name              = "S0"
  custom_subdomain_name = "rkwjournalreaderprediction"

  tags = local.tags
}

resource "azurerm_key_vault_secret" "api_key_1_prediction" {
  name         = "api-key-1-prediction"
  value        = azurerm_cognitive_account.rkw-prediction.primary_access_key
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_key_vault_secret" "api_key_2_prediction" {
  name         = "api-key-2-prediction"
  value        = azurerm_cognitive_account.rkw-prediction.secondary_access_key
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

resource "azurerm_key_vault_secret" "endpoint_prediction" {
  name         = "api-endpoint-prediction"
  value        = azurerm_cognitive_account.rkw-prediction.endpoint
  key_vault_id = azurerm_key_vault.rkw.id
  tags         = local.tags
}

