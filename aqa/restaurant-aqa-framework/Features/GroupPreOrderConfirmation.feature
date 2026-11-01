@ui
Feature: Confirming a group pre-order

  The reservation owner reviews the participants' baskets and submits one consolidated pre-order
  through the storefront. Test data is prepared through the API; actions under test use the UI.

  Background:
    Given a customer owns a group reservation
    And the owner signs in through the interface

  Rule: The owner transfers confirmed baskets to the restaurant

    @smoke @group-preorder-confirmation
    Scenario: The owner submits the consolidated baskets and the waiter sees the order
      Given an order exists for the group reservation
      And guest "AQA-Sample-Alice" adds 2 portions and submits through the invite page
      And guest "AQA-Sample-Bob" adds 3 portions and submits through the invite page
      And guest "AQA-Sample-Carol" adds 5 portions through the invite page
      And the owner's basket contains 1 confirmed portion
      When the owner opens the group pre-order from their reservation
      And the owner submits the consolidated pre-order through the interface
      Then the owner sees that the pre-order was sent to the restaurant
      And the group booking and order are submitted
      And the waiter sees 6 portions in the submitted order

  Rule: Invalid confirmations are prevented or reported to the owner

    @negative
    Scenario: The owner cannot submit when nobody confirmed a basket
      When the owner opens the group pre-order from their reservation
      Then the submit action is disabled with the no-dishes message

    @negative
    Scenario: The owner cannot submit when confirmed baskets contain no dishes
      Given a guest has confirmed an empty basket through test setup
      When the owner opens the group pre-order from their reservation
      Then the submit action is disabled with the no-dishes message

    @negative
    Scenario: The interface reports that the order was not found
      Given guest "AQA-Sample-Alice" adds 1 portion and submits through the invite page
      When the owner opens the group pre-order from their reservation
      And the owner submits the consolidated pre-order through the interface
      Then the owner sees the error "Order not found."

    @negative
    Scenario: The interface reports that the reservation was not found
      When the owner opens a group pre-order for a reservation that does not exist
      Then the group pre-order page reports "Reservation not found."

    @negative
    Scenario: The interface reports that the group reservation was not found
      When the owner opens a reservation without a group pre-order
      Then the group pre-order page reports "Group reservation was not found"

    @negative
    Scenario: A group pre-order cannot be submitted a second time
      Given an order exists for the group reservation
      And guest "AQA-Sample-Alice" adds 1 portion and submits through the invite page
      When the owner opens the group pre-order from their reservation
      And the owner submits the consolidated pre-order through the interface
      Then the owner sees that the pre-order was sent to the restaurant
      When the owner opens the group pre-order from their reservation
      Then the submit action is unavailable and the status is "Sent to Restaurant"

    @negative
    Scenario: A customer who is not the owner cannot review or submit the group
      When another customer opens the group pre-order through the interface
      Then the group pre-order page reports "Only the reservation owner can perform this action."
