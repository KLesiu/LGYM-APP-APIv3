Feature: Lifecycle

@serial @Lifecycle
Scenario: lifecycle-probe-a
    Then the lifecycle stack is ready
    And the lifecycle browser storage is empty

@serial @Lifecycle
Scenario: lifecycle-probe-b
    Then the lifecycle stack is ready
    And the lifecycle browser storage is empty
